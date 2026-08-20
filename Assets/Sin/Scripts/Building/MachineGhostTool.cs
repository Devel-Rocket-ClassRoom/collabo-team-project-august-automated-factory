using Factory.Buildings;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 팔레트에서 기계를 고르면 반투명 고스트가 손가락 위(스크린 오프셋만큼 띄운 위치)에 스냅되고,
    // 확인 버튼을 눌러야 실제로 배치된다 (터치 릴리즈만으로는 확정하지 않음 — 오조작 방지).
    public class MachineGhostTool : MonoBehaviour, IBuildTool
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private Vector2 screenOffset = new Vector2(0f, 150f);
        [SerializeField] private Color validColor = new Color(0.3f, 0.9f, 0.4f, 0.45f);
        [SerializeField] private Color invalidColor = new Color(0.9f, 0.2f, 0.2f, 0.45f);
        [SerializeField] private GameObject ghostPrefab;
        [SerializeField] private GameObject minerPrefab;
        [SerializeField] private GameObject processorPrefab;

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        private MachineDef selectedMachine;
        private GameObject ghost;
        private Vector2Int currentCell;
        private bool hasValidCell;

        public bool IsPlacing => selectedMachine != null;

        // 에디터 SerializedObject 없이(런타임/테스트에서) 직접 배선할 때 쓴다.
        public void Initialize(Camera targetCamera, SimulationDriver driver)
        {
            this.targetCamera = targetCamera;
            this.driver = driver;
        }

        public void SelectMachine(MachineDef machineDef)
        {
            CancelPlacement();
            selectedMachine = machineDef;

            // 고스트는 실제로 놓일 기계와 같은 모양을 쓰고, 색만 유효/무효 색으로 덮어씌운다
            // (지금은 전부 큐브라 티가 안 나지만, 모양이 갈리기 시작하면 의미가 생긴다).
            GameObject shapePrefab = machineDef.category == MachineCategory.Miner ? minerPrefab : processorPrefab;
            ghost = shapePrefab != null
                ? Instantiate(shapePrefab, transform)
                : ghostPrefab != null
                    ? Instantiate(ghostPrefab, transform)
                    : BuildVisuals.CreateBox(Vector3.zero, new Vector3(0.9f, 0.9f, 0.9f), invalidColor, transform, withCollider: false);

            // 미리보기 전용이라 실제 동작(MachineView)과 충돌 판정은 걷어낸다.
            var view = ghost.GetComponent<MachineView>();
            if (view != null) Destroy(view);
            var collider = ghost.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            ghost.SetActive(false);

            // 팔레트 버튼만 누르고 화면을 아직 탭하지 않은 상태에서도 바로 눈에 보이도록,
            // 화면 중앙 기준으로 즉시 한 번 배치해준다 (오프셋 없이 — 아직 손가락이 없으므로).
            if (targetCamera != null)
            {
                PlaceGhostAlongRay(targetCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)));
            }
        }

        public void CancelPlacement()
        {
            selectedMachine = null;
            hasValidCell = false;
            if (ghost != null) Destroy(ghost);
            ghost = null;
        }

        public void OnPressBegin(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnDrag(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnReleased(Vector2 screenPosition) => UpdateGhost(screenPosition);
        public void OnCancelled() { }

        private void UpdateGhost(Vector2 screenPosition)
        {
            if (selectedMachine == null || targetCamera == null) return;
            PlaceGhostAlongRay(targetCamera.ScreenPointToRay(screenPosition + screenOffset));
        }

        private void PlaceGhostAlongRay(Ray ray)
        {
            if (ghost == null) return;

            if (!GridUtility.TryRaycastToCell(ray, groundPlane, out currentCell))
            {
                hasValidCell = false;
                ghost.SetActive(false);
                return;
            }

            hasValidCell = true;

            ghost.SetActive(true);
            ghost.transform.position = GridUtility.CellToWorldCenter(currentCell, 0.5f);

            bool free = driver == null || driver.World == null || !driver.World.Grid.IsOccupied(currentCell);
            BuildVisuals.Colorize(ghost, free ? validColor : invalidColor);
        }

        // 확인 버튼에서 호출.
        public bool Confirm()
        {
            if (selectedMachine == null || !hasValidCell || driver == null || driver.World == null) return false;

            var grid = driver.World.Grid;
            if (grid.IsOccupied(currentCell)) return false;

            var db = driver.World.Database;
            if (!db.TryGetMachineId(selectedMachine.machineId, out int machineId)) return false;

            Vector3 worldPos = GridUtility.CellToWorldCenter(currentCell, 0.5f);

            if (selectedMachine.category == MachineCategory.Miner)
            {
                var runtime = db.Machines[machineId];
                if (runtime.MinerOutputResourceId < 0) return false;

                var miner = new MinerInstance { MachineId = machineId, OutputResourceId = runtime.MinerOutputResourceId };
                int index = driver.World.AddMiner(miner);
                grid.RegisterBuilding(currentCell, CellOccupantType.Miner, index);
                TryAutoConnectAdjacentBelts(currentCell, MachineInstanceKind.Miner, index);
                SpawnMachineVisual(minerPrefab, worldPos, MachineInstanceKind.Miner, index, new Color(0.55f, 0.4f, 0.25f));
            }
            else
            {
                var processor = new ProcessorInstance(db.ResourceCount) { MachineId = machineId };
                if (db.TryGetFirstRecipeForCategory(selectedMachine.category, out int recipeId))
                {
                    processor.RecipeId = recipeId;
                }
                int index = driver.World.AddProcessor(processor);
                grid.RegisterBuilding(currentCell, CellOccupantType.Processor, index);
                TryAutoConnectAdjacentBelts(currentCell, MachineInstanceKind.Processor, index);
                SpawnMachineVisual(processorPrefab, worldPos, MachineInstanceKind.Processor, index, new Color(0.6f, 0.15f, 0.1f));
            }

            CancelPlacement();
            return true;
        }

        // 벨트를 먼저 뻗어두고 나중에 그 옆에 기계를 놓는 순서로 지어도 연결되도록, 새로 놓인
        // 기계의 4방향 인접 칸에 "아직 연결 안 된" 벨트 세그먼트가 있으면 자동으로 이어준다.
        // (벨트를 놓을 때는 반대 방향 — 옆에 있는 기계에 연결하는 것 — 을 BeltDragTool이
        // 이미 처리하고 있음. 이 메서드는 그 반대 순서를 처리하는 대응쌍이다.)
        private void TryAutoConnectAdjacentBelts(Vector2Int cell, MachineInstanceKind kind, int index)
        {
            var grid = driver.World.Grid;
            var segments = driver.World.Segments;
            Vector2Int[] neighbors = { cell + Vector2Int.up, cell + Vector2Int.down, cell + Vector2Int.left, cell + Vector2Int.right };

            foreach (var neighbor in neighbors)
            {
                if (!grid.TryGetOccupant(neighbor, out var occupant) || occupant.Type != CellOccupantType.Belt) continue;

                var segment = segments[occupant.InstanceIndex];

                if (kind == MachineInstanceKind.Processor)
                {
                    bool isDeadEnd = segment.NextSegmentId == null && segment.TargetProcessorId == null;
                    if (isDeadEnd) segment.TargetProcessorId = index;
                }
                else
                {
                    bool hasNoSource = segment.SourceMinerId == null && segment.SourceProcessorId == null;
                    bool isChainStart = hasNoSource && !IsTargetOfAnySegment(segments, segment.Id);
                    if (isChainStart) segment.SourceMinerId = index;
                }
            }
        }

        private static bool IsTargetOfAnySegment(System.Collections.Generic.List<BeltSegment> segments, int segmentId)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].NextSegmentId == segmentId) return true;
            }
            return false;
        }

        private void SpawnMachineVisual(GameObject prefab, Vector3 position, MachineInstanceKind kind, int index, Color color)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, position, Quaternion.identity);
            }
            else
            {
                go = BuildVisuals.CreateBox(position, Vector3.one, color, null);
                go.AddComponent<MachineView>();
            }
            go.name = $"{kind}_{index}";

            var view = go.GetComponent<MachineView>();
            view.Initialize(kind, index, driver);
        }
    }
}
