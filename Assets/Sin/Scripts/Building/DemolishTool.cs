using System.Collections.Generic;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 드래그로 사각 영역을 훑어서 그 안의 기계/벨트를 한 번에 여러 개 선택하고, "철거 확정"
    // 버튼을 눌러야 실제로 지운다(터치 릴리즈만으로 바로 지우면 큰 영역을 잘못 훑었을 때
    // 되돌릴 방법이 없어서 오조작 피해가 큼 — 기계 배치가 확정 버튼을 거치는 것과 같은 이유).
    // 코어는 선택 대상에서 항상 제외한다 — 지워지면 게임이 통째로 망가진다.
    public class DemolishTool : MonoBehaviour, IBuildTool
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private BuildInputRouter router;
        [SerializeField] private Color previewColor = new Color(0.9f, 0.15f, 0.15f, 0.4f);

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly HashSet<(CellOccupantType type, int index)> selected = new HashSet<(CellOccupantType, int)>();

        private GameObject selectionBox;
        private Vector2Int startCell;
        private bool dragging;

        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        private void OnEnable()
        {
            if (router != null) router.ModeChanged += HandleModeChanged;
        }

        private void OnDisable()
        {
            if (router != null) router.ModeChanged -= HandleModeChanged;
        }

        // 다른 도구로 전환되면(확정 안 하고 팔레트에서 다른 버튼 누름 등) 남아있던 빨간
        // 선택 박스가 화면에 계속 떠 있으면 안 되니 같이 지운다.
        private void HandleModeChanged(BuildInputRouter.Mode mode)
        {
            if (mode != BuildInputRouter.Mode.Demolish) ClearSelection();
        }

        public void OnPressBegin(Vector2 screenPosition)
        {
            if (!TryScreenToCell(screenPosition, out startCell)) return;
            dragging = true;
            RebuildSelection(startCell);
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!dragging) return;
            if (!TryScreenToCell(screenPosition, out var cell)) return;
            RebuildSelection(cell);
        }

        public void OnReleased(Vector2 screenPosition)
        {
            // 릴리즈로 바로 지우지 않는다 — 선택 상태만 유지하고 "철거 확정" 버튼을 기다린다.
            dragging = false;
        }

        public void OnCancelled()
        {
            dragging = false;
            ClearSelection();
        }

        private bool TryScreenToCell(Vector2 screenPosition, out Vector2Int cell)
        {
            cell = default;
            if (targetCamera == null) return false;
            return GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane, out cell);
        }

        private void RebuildSelection(Vector2Int currentCell)
        {
            selected.Clear();
            if (driver == null || driver.World == null) return;

            int minX = Mathf.Min(startCell.x, currentCell.x);
            int maxX = Mathf.Max(startCell.x, currentCell.x);
            int minY = Mathf.Min(startCell.y, currentCell.y);
            int maxY = Mathf.Max(startCell.y, currentCell.y);

            var grid = driver.World.Grid;
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (!grid.TryGetOccupant(new Vector2Int(x, y), out var occupant)) continue;
                    if (IsCore(occupant)) continue; // 코어는 절대 선택되지 않는다.
                    selected.Add((occupant.Type, occupant.InstanceIndex));
                }
            }

            UpdateSelectionBoxVisual(minX, maxX, minY, maxY);
        }

        private bool IsCore(CellOccupant occupant)
        {
            return occupant.Type == CellOccupantType.Processor && occupant.InstanceIndex == driver.World.CoreProcessorIndex;
        }

        private void UpdateSelectionBoxVisual(int minX, int maxX, int minY, int maxY)
        {
            if (selectionBox == null)
            {
                selectionBox = BuildVisuals.CreateBox(Vector3.zero, Vector3.one, previewColor, transform, withCollider: false);
            }

            float sizeX = (maxX - minX + 1) * GridUtility.CellSize;
            float sizeZ = (maxY - minY + 1) * GridUtility.CellSize;
            Vector3 center = new Vector3(
                (minX + maxX + 1) * 0.5f * GridUtility.CellSize,
                0.05f,
                (minY + maxY + 1) * 0.5f * GridUtility.CellSize);

            selectionBox.transform.position = center;
            selectionBox.transform.localScale = new Vector3(sizeX, 0.1f, sizeZ);
            selectionBox.SetActive(true);
        }

        private void ClearSelection()
        {
            selected.Clear();
            if (selectionBox != null) selectionBox.SetActive(false);
        }

        // "철거 확정" 버튼에서 호출.
        public bool Confirm()
        {
            if (driver == null || driver.World == null || selected.Count == 0) return false;

            var world = driver.World;
            var grid = world.Grid;

            foreach (var (type, index) in selected)
            {
                switch (type)
                {
                    case CellOccupantType.Miner:
                        DestroyVisual($"{MachineInstanceKind.Miner}_{index}");
                        grid.UnregisterOccupant(type, index);
                        world.RemoveMiner(index);
                        break;
                    case CellOccupantType.Processor:
                        DestroyVisual($"{MachineInstanceKind.Processor}_{index}");
                        grid.UnregisterOccupant(type, index);
                        world.RemoveProcessor(index);
                        break;
                    case CellOccupantType.Belt:
                        DestroyVisual($"Belt_{index}");
                        grid.UnregisterOccupant(type, index);
                        world.RemoveSegment(index);
                        break;
                }
            }

            ClearSelection();
            return true;
        }

        // 벨트/기계 스폰 시 지어진 이름 규칙(BeltDragTool.SpawnCommittedVisual, MachineGhostTool.
        // SpawnMachineVisual 참고)을 그대로 재사용해서 찾는다 — 인덱스→비주얼 역방향 레지스트리를
        // 따로 안 두려고 일부러 이렇게 함(철거는 드문 조작이라 이름 검색 비용도 무방).
        private static void DestroyVisual(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) Destroy(go);
        }
    }
}
