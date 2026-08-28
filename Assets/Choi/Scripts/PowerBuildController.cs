using System.Collections.Generic;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Choi.SaveLoad
{
    public enum PowerBuildMode
    {
        None,
        Generator,
        Cable,
        TransmissionTower,
        Remove,
    }

    /// <summary>기존 BuildInputRouter를 수정하지 않고, 선택 중에만 잠시 비활성화하는 전력 배치 도구입니다.</summary>
    public sealed class PowerBuildController : MonoBehaviour
    {
        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<GameObject> visuals = new List<GameObject>();

        private PowerGridSystem powerGrid;
        private SimulationDriver driver;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        private Camera targetCamera;

        public PowerBuildMode Mode { get; private set; }
        public string LastMessage { get; private set; } = "전력 도구 대기";

        private void Awake()
        {
            powerGrid = GetComponent<PowerGridSystem>() ?? FindAnyObjectByType<PowerGridSystem>();
            driver = FindAnyObjectByType<SimulationDriver>();
            buildRouter = FindAnyObjectByType<BuildInputRouter>();
            machineTool = FindAnyObjectByType<MachineGhostTool>();
            targetCamera = Camera.main;
        }

        private void Start()
        {
            RebuildVisuals();
        }

        private void Update()
        {
            if (Mode == PowerBuildMode.None) return;

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetMode(PowerBuildMode.None);
                return;
            }

            if (!TryGetPointerPress(out Vector2 screenPosition, out int? pointerId)) return;
            if (IsOverUi(pointerId)) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            if (!GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane, out Vector2Int cell)) return;
            ApplyAt(cell);
        }

        private void OnDisable()
        {
            RestoreBuildRouter();
        }

        public void SetMode(PowerBuildMode mode)
        {
            Mode = mode;
            if (mode == PowerBuildMode.None)
            {
                RestoreBuildRouter();
                LastMessage = "전력 배치 종료";
                return;
            }

            machineTool?.CancelPlacement();
            if (buildRouter != null)
            {
                buildRouter.SetMode(BuildInputRouter.Mode.None);
                buildRouter.enabled = false;
            }
            LastMessage = mode == PowerBuildMode.Generator ? "발전기를 놓을 칸을 선택하세요"
                : mode == PowerBuildMode.Cable ? "전선을 이어 놓으세요"
                : mode == PowerBuildMode.TransmissionTower ? "송신탑을 놓을 빈 칸을 선택하세요"
                : "철거할 발전기/전선/송신탑을 선택하세요";
        }

        public void RebuildVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i] != null) Destroy(visuals[i]);
            }
            visuals.Clear();

            if (powerGrid == null) return;
            var nodesByCell = new Dictionary<Vector2Int, PowerNodeRuntime>();
            for (int i = 0; i < powerGrid.Nodes.Count; i++) nodesByCell[powerGrid.Nodes[i].Cell] = powerGrid.Nodes[i];

            for (int i = 0; i < powerGrid.Nodes.Count; i++)
            {
                PowerNodeRuntime node = powerGrid.Nodes[i];
                float height = node.Kind == PowerNodeKind.Generator ? 0.5f
                    : node.Kind == PowerNodeKind.TransmissionTower ? 1.15f : 0.22f;
                Vector3 center = GridUtility.CellToWorldCenter(node.Cell, height);

                GameObject visual;
                if (node.Kind == PowerNodeKind.Generator)
                {
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    visual.transform.position = center;
                    visual.transform.localScale = new Vector3(0.72f, 0.5f, 0.72f);
                    BuildVisuals.Colorize(visual, new Color(1f, 0.62f, 0.08f));
                }
                else if (node.Kind == PowerNodeKind.Cable)
                {
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.transform.position = center;
                    visual.transform.localScale = new Vector3(0.28f, 0.42f, 0.28f);
                    BuildVisuals.Colorize(visual, new Color(0.1f, 0.82f, 1f));
                }
                else
                {
                    visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    visual.transform.position = center;
                    visual.transform.localScale = new Vector3(0.42f, 1.05f, 0.42f);
                    BuildVisuals.Colorize(visual, new Color(0.72f, 0.25f, 1f));
                }

                Destroy(visual.GetComponent<Collider>());
                visual.name = $"PowerNode_{node.Id}_{node.Kind}";
                visuals.Add(visual);

                Vector2Int right = node.Cell + Vector2Int.right;
                Vector2Int up = node.Cell + Vector2Int.up;
                if (nodesByCell.TryGetValue(right, out PowerNodeRuntime rightNode)
                    && (node.Kind == PowerNodeKind.Cable || rightNode.Kind == PowerNodeKind.Cable))
                    CreateWire(node.Cell, right);
                if (nodesByCell.TryGetValue(up, out PowerNodeRuntime upNode)
                    && (node.Kind == PowerNodeKind.Cable || upNode.Kind == PowerNodeKind.Cable))
                    CreateWire(node.Cell, up);
            }
        }

        private void ApplyAt(Vector2Int cell)
        {
            if (powerGrid == null) return;

            bool changed = false;
            switch (Mode)
            {
                case PowerBuildMode.Generator:
                    if (driver != null && driver.World != null && driver.World.Grid.IsOccupied(cell))
                    {
                        LastMessage = "발전기는 빈 칸에만 놓을 수 있습니다";
                        return;
                    }
                    changed = powerGrid.TryAddNode(PowerNodeKind.Generator, cell);
                    LastMessage = changed ? $"발전기 설치: {cell}" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.Cable:
                    changed = powerGrid.TryAddNode(PowerNodeKind.Cable, cell);
                    LastMessage = changed ? $"전선 설치: {cell}" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.TransmissionTower:
                    if (driver != null && driver.World != null && driver.World.Grid.IsOccupied(cell))
                    {
                        LastMessage = "송신탑은 빈 칸에만 놓을 수 있습니다";
                        return;
                    }
                    changed = powerGrid.TryAddNode(PowerNodeKind.TransmissionTower, cell);
                    LastMessage = changed ? $"송신탑 설치: {cell} · 공급 범위 3x3" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.Remove:
                    changed = powerGrid.RemoveNode(cell);
                    LastMessage = changed ? $"전력 시설 철거: {cell}" : "철거할 전력 시설이 없습니다";
                    break;
            }

            if (changed)
            {
                RebuildVisuals();
                powerGrid.EvaluatePower();
            }
        }

        private void CreateWire(Vector2Int fromCell, Vector2Int toCell)
        {
            Vector3 from = GridUtility.CellToWorldCenter(fromCell, 0.32f);
            Vector3 to = GridUtility.CellToWorldCenter(toCell, 0.32f);
            GameObject wire = BuildVisuals.CreateStrip(from, to, 0.09f, new Color(0.05f, 0.65f, 0.9f), null, false);
            wire.name = "PowerWire";
            visuals.Add(wire);
        }

        private void RestoreBuildRouter()
        {
            if (buildRouter != null) buildRouter.enabled = true;
        }

        private static bool TryGetPointerPress(out Vector2 position, out int? pointerId)
        {
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    if (!touches[i].press.wasPressedThisFrame) continue;
                    position = touches[i].position.ReadValue();
                    pointerId = touches[i].touchId.ReadValue();
                    return true;
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = null;
                return true;
            }

            position = default;
            pointerId = null;
            return false;
        }

        private static bool IsOverUi(int? pointerId)
        {
            if (EventSystem.current == null) return false;
            return pointerId.HasValue
                ? EventSystem.current.IsPointerOverGameObject(pointerId.Value)
                : EventSystem.current.IsPointerOverGameObject();
        }
    }
}
