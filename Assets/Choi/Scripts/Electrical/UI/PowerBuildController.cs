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
        private const float CableHeight = 1.65f;
        private const float CableWidth = 0.035f;
        private const int TowerRangeRadius = 7;
        private static readonly Vector2Int NodePointerOffset = new Vector2Int(0, 2);

        private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        private readonly List<GameObject> visuals = new List<GameObject>();
        private readonly List<GameObject> placementPreview = new List<GameObject>();
        private readonly List<GameObject> towerSelectionPreview = new List<GameObject>();
        private readonly List<GameObject> coreRangePreview = new List<GameObject>();
        private readonly List<Vector2Int> cableDragPath = new List<Vector2Int>();

        private PowerGridSystem powerGrid;
        private SimulationDriver driver;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        private Camera targetCamera;
        private bool isCableDragging;
        private Vector2Int cableStartCell;
        private PowerNodeRuntime cableStartNode;
        private bool lastBlackoutState;
        private bool blackoutStateInitialized;
        private int selectedTowerId = -1;
        private bool isCoreRangeSelected;
        private GameObject nodePlacementGhost;
        private bool hasPendingNodePlacement;
        private bool isDraggingNodePlacement;
        private Vector2Int pendingNodeCell;

        public PowerBuildMode Mode { get; private set; }
        public string LastMessage { get; private set; } = "전력 도구 대기";
        public bool HasPendingNodePlacement => hasPendingNodePlacement
            && (Mode == PowerBuildMode.Generator || Mode == PowerBuildMode.TransmissionTower);

        private void Awake()
        {
            powerGrid = GetComponent<PowerGridSystem>() ?? FindAnyObjectByType<PowerGridSystem>();
            driver = FindAnyObjectByType<SimulationDriver>();
            buildRouter = FindAnyObjectByType<BuildInputRouter>();
            machineTool = FindAnyObjectByType<MachineGhostTool>();
            targetCamera = Camera.main;
            BeltDragTool.ExternalCellBlocked = IsPowerStructureCell;
            DemolishTool.ExternalConfirm = RemovePowerInArea;
            DemolishTool.ExternalHasTargets = HasPowerInArea;
        }

        private void Start()
        {
            RebuildVisuals();
        }

        private void Update()
        {
            if (!TryGetPointerState(out Vector2 screenPosition, out int? pointerId,
                    out bool pressed, out bool held, out bool released))
            {
                if (Mode != PowerBuildMode.Generator && Mode != PowerBuildMode.TransmissionTower)
                    ClearPlacementPreview();
                return;
            }

            if (IsOverUi(pointerId) && !isCableDragging)
            {
                if (Mode != PowerBuildMode.None && Mode != PowerBuildMode.Generator
                    && Mode != PowerBuildMode.TransmissionTower) ClearPlacementPreview();
                return;
            }

            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            if (!GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenPosition), groundPlane,
                    out Vector2Int cell))
            {
                ClearPlacementPreview();
                return;
            }

            if (Mode == PowerBuildMode.None)
            {
                if (pressed) SelectPowerRangeAt(cell);
                return;
            }

            if (Mode == PowerBuildMode.Cable)
            {
                HandleCablePlacement(cell, pressed, held, released);
                return;
            }

            if (Mode == PowerBuildMode.Generator)
            {
                HandleNodePlacementDrag(cell, pressed, held, released);
                return;
            }

            if (Mode == PowerBuildMode.TransmissionTower)
            {
                HandleNodePlacementDrag(cell, pressed, held, released);
                return;
            }
            if (Mode == PowerBuildMode.Remove)
            {
                ClearPlacementPreview();
                return; // 영역 선택과 확정은 공용 DemolishTool이 담당한다.
            }
            ClearPlacementPreview();

            if (pressed) ApplyAt(cell);
        }

        private void LateUpdate()
        {
            if (powerGrid == null) return;
            if (blackoutStateInitialized && lastBlackoutState == powerGrid.IsBlackout) return;
            blackoutStateInitialized = true;
            lastBlackoutState = powerGrid.IsBlackout;
            RebuildVisuals();
        }

        private void OnDisable()
        {
            CancelPlacementPreview();
            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
            RestoreBuildRouter();
        }

        private void OnDestroy()
        {
            if (BeltDragTool.ExternalCellBlocked == IsPowerStructureCell)
                BeltDragTool.ExternalCellBlocked = null;
            if (DemolishTool.ExternalConfirm == RemovePowerInArea) DemolishTool.ExternalConfirm = null;
            if (DemolishTool.ExternalHasTargets == HasPowerInArea) DemolishTool.ExternalHasTargets = null;
        }

        public void ToggleMode(PowerBuildMode mode)
        {
            SetMode(Mode == mode ? PowerBuildMode.None : mode);
        }

        public void SetMode(PowerBuildMode mode)
        {
            CancelPlacementPreview();
            ClearPowerRangeSelection();
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
                // 노드 배치는 고스트를 잡고 있을 때만 라우터를 잠근다. 그 외에는 다른 건설
                // 도구처럼 배경 드래그로 카메라를 이동할 수 있다. 전선은 자체 드래그 도구라 잠근다.
                buildRouter.enabled = mode != PowerBuildMode.Cable;
            }
            if (mode == PowerBuildMode.Generator || mode == PowerBuildMode.TransmissionTower)
                BeginNodePlacement(mode);
            LastMessage = mode == PowerBuildMode.Generator ? "좌클릭 드래그로 발전기를 옮기고 확정하세요 · 우클릭 드래그는 화면 이동"
                : mode == PowerBuildMode.Cable ? "시작점에서 끝점까지 드래그해 전선을 이으세요"
                : mode == PowerBuildMode.TransmissionTower ? "좌클릭 드래그로 송전탑을 옮기고 확정하세요 · 우클릭 드래그는 화면 이동"
                : "철거할 발전기/전선/송전탑을 선택하세요";
        }

        public void RebuildVisuals()
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                if (visuals[i] != null) Destroy(visuals[i]);
            }
            visuals.Clear();

            if (powerGrid == null) return;
            for (int i = 0; i < powerGrid.Nodes.Count; i++)
            {
                PowerNodeRuntime node = powerGrid.Nodes[i];
                float height = node.Kind == PowerNodeKind.Generator ? 0.5f
                    : node.Kind == PowerNodeKind.TransmissionTower ? 1.15f : 0.035f;
                Vector3 center = GridUtility.CellToWorldCenter(node.Cell, height);

                GameObject visual;
                if (node.Kind == PowerNodeKind.Generator)
                {
                    visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    visual.transform.position = center;
                    visual.transform.localScale = new Vector3(0.72f, 0.5f, 0.72f);
                    BuildVisuals.Colorize(visual, powerGrid.IsBlackout
                        ? new Color(0.24f, 0.12f, 0.1f)
                        : new Color(1f, 0.62f, 0.08f));
                }
                else if (node.Kind == PowerNodeKind.Cable || node.Kind == PowerNodeKind.Junction)
                {
                    // 전선은 칸마다 오브젝트를 보이지 않고, 인접 칸 사이의 얇은 선만 렌더링한다.
                    visual = null;
                }
                else
                {
                    visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    visual.transform.position = center;
                    visual.transform.localScale = new Vector3(0.42f, 1.05f, 0.42f);
                    BuildVisuals.Colorize(visual, new Color(0.72f, 0.25f, 1f));
                }

                if (visual != null)
                {
                    Destroy(visual.GetComponent<Collider>());
                    visual.name = $"PowerNode_{node.Id}_{node.Kind}";
                    visuals.Add(visual);
                }

            }

            for (int i = 0; i < powerGrid.Connections.Count; i++)
            {
                PowerConnectionRuntime connection = powerGrid.Connections[i];
                CreateConnectionWire(connection.Path);
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
                    LastMessage = "발전기 또는 송전탑에서 드래그해 연결하세요";
                    break;
                case PowerBuildMode.TransmissionTower:
                    if (driver != null && driver.World != null && driver.World.Grid.IsOccupied(cell))
                    {
                        LastMessage = "송전탑은 빈 칸에만 놓을 수 있습니다";
                        return;
                    }
                    changed = powerGrid.TryAddNode(PowerNodeKind.TransmissionTower, cell);
                    LastMessage = changed ? $"송전탑 설치: {cell} · 공급 범위 15x15" : "이미 전력 시설이 있는 칸입니다";
                    break;
                case PowerBuildMode.Remove:
                    changed = powerGrid.RemoveNode(cell) || powerGrid.RemoveConnectionAt(cell);
                    LastMessage = changed ? $"전력 시설 철거: {cell}" : "철거할 전력 시설이 없습니다";
                    break;
            }

            if (changed)
            {
                PowerBuildMode completedMode = Mode;
                string completedMessage = LastMessage;
                RebuildVisuals();
                powerGrid.EvaluatePower();
                // 발전기/송전탑 모두 취소할 때까지 같은 배치 모드를 유지한다.
                if (completedMode == PowerBuildMode.Generator || completedMode == PowerBuildMode.TransmissionTower)
                {
                    BeginNodePlacement(completedMode);
                    LastMessage = completedMessage + " · 계속 배치하거나 취소로 종료하세요";
                }
            }
        }

        public bool ConfirmPendingPlacement()
        {
            if (!HasPendingNodePlacement) return false;
            if (!IsNodePlacementValid(pendingNodeCell))
            {
                LastMessage = "전력 시설은 비어 있는 칸에만 놓을 수 있습니다";
                return false;
            }
            ApplyAt(pendingNodeCell);
            return true;
        }

        private void BeginNodePlacement(PowerBuildMode mode)
        {
            if (targetCamera == null) targetCamera = Camera.main;
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (targetCamera != null
                && GridUtility.TryRaycastToCell(targetCamera.ScreenPointToRay(screenCenter), groundPlane, out var centerCell))
                pendingNodeCell = centerCell + NodePointerOffset;
            else pendingNodeCell = Vector2Int.zero;
            hasPendingNodePlacement = true;
            isDraggingNodePlacement = false;
            RebuildNodePlacementGhost(mode);
        }

        private void HandleNodePlacementDrag(Vector2Int cell, bool pressed, bool held, bool released)
        {
            if (!hasPendingNodePlacement) BeginNodePlacement(Mode);

            // 고스트를 정확히 집을 필요 없이, 월드 아무 곳에서 좌클릭 드래그를 시작하면
            // 일반 기계 배치처럼 고스트가 포인터를 따라간다. 우클릭은 이 코드가 소비하지
            // 않으므로 BuildInputRouter의 카메라 팬으로 그대로 전달된다.
            if (pressed)
            {
                isDraggingNodePlacement = true;
                pendingNodeCell = cell + NodePointerOffset;
                RebuildNodePlacementGhost(Mode);
                if (buildRouter != null)
                {
                    buildRouter.SetMode(BuildInputRouter.Mode.None);
                    buildRouter.enabled = false;
                }
            }
            Vector2Int offsetCell = cell + NodePointerOffset;
            if (isDraggingNodePlacement && (held || released) && offsetCell != pendingNodeCell)
            {
                pendingNodeCell = offsetCell;
                RebuildNodePlacementGhost(Mode);
            }
            if (released && isDraggingNodePlacement)
            {
                isDraggingNodePlacement = false;
                RestoreBuildRouter();
            }
        }

        private void RebuildNodePlacementGhost(PowerBuildMode mode)
        {
            if (nodePlacementGhost != null) Destroy(nodePlacementGhost);
            bool valid = IsNodePlacementValid(pendingNodeCell);
            // 일반 기계 고스트(validColor/invalidColor)의 높은 알파값과 비슷하게 맞춰 밝은
            // 바닥에서도 형태와 유효 여부가 분명히 보이게 한다.
            Color color = valid ? new Color(0.3f, 0.9f, 0.4f, 0.85f) : new Color(0.9f, 0.2f, 0.2f, 0.85f);
            if (mode == PowerBuildMode.Generator)
            {
                nodePlacementGhost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                nodePlacementGhost.transform.position = GridUtility.CellToWorldCenter(pendingNodeCell, 0.5f);
                nodePlacementGhost.transform.localScale = new Vector3(0.72f, 0.5f, 0.72f);
            }
            else
            {
                nodePlacementGhost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                nodePlacementGhost.transform.position = GridUtility.CellToWorldCenter(pendingNodeCell, 1.15f);
                nodePlacementGhost.transform.localScale = new Vector3(0.42f, 1.05f, 0.42f);
                ClearPlacementPreview();
                CreateTowerRange(pendingNodeCell, placementPreview, new Color(0.35f, 0.9f, 1f));
            }
            nodePlacementGhost.name = "PowerNodePlacementGhost";
            Destroy(nodePlacementGhost.GetComponent<Collider>());
            BuildVisuals.Colorize(nodePlacementGhost, color);
        }

        private bool IsNodePlacementValid(Vector2Int cell)
        {
            if (powerGrid != null && powerGrid.IsCoreCell(cell)) return false;
            if (powerGrid != null && powerGrid.TryGetNode(cell, out _)) return false;
            if (driver == null || driver.World == null) return true;
            // 광물 노드는 WorldGrid의 건물 점유와 별도 레이어지만, 색깔 있는 광맥 위에
            // 전력 시설이 겹치면 채굴기를 영원히 놓지 못하므로 배치 불가 칸으로 취급한다.
            if (driver.World.Grid.TryGetOreDeposit(cell, out _)) return false;
            return !driver.World.Grid.IsOccupied(cell);
        }

        private bool IsPowerStructureCell(Vector2Int cell)
        {
            if (powerGrid == null || !powerGrid.TryGetNode(cell, out var node)) return false;
            return node.Kind == PowerNodeKind.Generator || node.Kind == PowerNodeKind.TransmissionTower;
        }

        private bool HasPowerInArea(RectInt bounds)
        {
            if (powerGrid == null) return false;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (powerGrid.TryGetNode(cell, out _)) return true;
                    for (int i = 0; i < powerGrid.Connections.Count; i++)
                    {
                        var path = powerGrid.Connections[i].Path;
                        if (ConnectionCrossesCell(path, cell)) return true;
                    }
                }
            }
            return false;
        }

        private bool RemovePowerInArea(RectInt bounds)
        {
            if (powerGrid == null) return false;
            bool changed = false;
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (powerGrid.RemoveNode(cell)) changed = true;
                    while (powerGrid.RemoveConnectionAt(cell)) changed = true;
                }
            }
            if (changed)
            {
                RebuildVisuals();
                powerGrid.EvaluatePower();
                LastMessage = "선택 영역의 전력 시설을 철거했습니다";
            }
            return changed;
        }

        private static bool ConnectionCrossesCell(IReadOnlyList<Vector2Int> path, Vector2Int cell)
        {
            if (path == null) return false;
            for (int i = 0; i < path.Count; i++)
                if (path[i] == cell) return true;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int from = path[i];
                Vector2Int to = path[i + 1];
                long dx = to.x - from.x;
                long dy = to.y - from.y;
                long px = cell.x - from.x;
                long py = cell.y - from.y;
                if (dx * py == dy * px
                    && cell.x >= Mathf.Min(from.x, to.x) && cell.x <= Mathf.Max(from.x, to.x)
                    && cell.y >= Mathf.Min(from.y, to.y) && cell.y <= Mathf.Max(from.y, to.y))
                    return true;
            }
            return false;
        }

        private void CreateWire(Vector2Int fromCell, Vector2Int toCell)
        {
            // 탑뷰에서 건물 위를 가로지르는 가는 전선처럼 보이게 한다.
            Vector3 from = GridUtility.CellToWorldCenter(fromCell, CableHeight);
            Vector3 to = GridUtility.CellToWorldCenter(toCell, CableHeight);
            GameObject wire = BuildVisuals.CreateStrip(from, to, CableWidth,
                new Color(0.03f, 0.58f, 0.82f), null, false);
            wire.name = "PowerWire";
            visuals.Add(wire);
        }

        private void CreateConnectionWire(List<Vector2Int> path)
        {
            if (path == null) return;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i - 1] != path[i]) CreateWire(path[i - 1], path[i]);
            }
        }

        private void HandleCablePlacement(Vector2Int cell, bool pressed, bool held, bool released)
        {
            if (pressed && !isCableDragging)
            {
                if (powerGrid == null || !powerGrid.TryResolveConnectionPoint(cell, out cableStartNode))
                {
                    LastMessage = "발전기, 송전탑 또는 기존 전선에서 드래그를 시작하세요";
                    ClearPlacementPreview();
                    return;
                }
                isCableDragging = true;
                cableStartCell = cell;
                cableDragPath.Clear();
                cableDragPath.Add(cell);
            }

            if (isCableDragging && (held || released))
            {
                AppendDragCell(cell);
                ShowCablePreview(cell);
            }
            if (!isCableDragging || !released) return;

            if (cableStartCell == cell || !powerGrid.TryResolveConnectionPoint(cell, out PowerNodeRuntime endNode))
            {
                isCableDragging = false;
                ClearPlacementPreview();
                cableDragPath.Clear();
                cableStartNode = null;
                LastMessage = "다른 발전기, 송전탑 또는 기존 전선에서 드래그를 끝내세요";
                return;
            }
            if (!powerGrid.CanConnect(cableStartNode, endNode))
            {
                isCableDragging = false;
                ClearPlacementPreview();
                cableDragPath.Clear();
                cableStartNode = null;
                LastMessage = "이 송전탑에는 이미 발전기 하나가 직접 연결되어 있습니다";
                return;
            }

            List<Vector2Int> finalPath = IsGeneratorTowerPair(cableStartNode, endNode)
                ? new List<Vector2Int> { cableStartNode.Cell, endNode.Cell }
                : new List<Vector2Int>(cableDragPath);
            bool installed = powerGrid.TryAddConnection(cableStartNode, endNode, finalPath);

            isCableDragging = false;
            ClearPlacementPreview();
            cableDragPath.Clear();
            cableStartNode = null;
            if (installed)
            {
                string completedMessage = $"전력 시설 연결: {cableStartCell} → {cell}";
                RebuildVisuals();
                powerGrid.EvaluatePower();
                SetMode(PowerBuildMode.None);
                LastMessage = completedMessage;
            }
            else
            {
                LastMessage = "두 전력 시설은 이미 직접 연결되어 있습니다";
            }
        }

        private void AppendDragCell(Vector2Int cell)
        {
            if (cableDragPath.Count == 0 || cableDragPath[cableDragPath.Count - 1] != cell)
                cableDragPath.Add(cell);
        }

        private void ShowCablePreview(Vector2Int currentCell)
        {
            ClearPlacementPreview();
            if (cableStartNode == null || cableStartCell == currentCell) return;

            // 드래그 경로의 흔들림과 관계없이 시작점-현재 지점을 한 직선으로 미리 보여준다.
            Vector3 from = GridUtility.CellToWorldCenter(cableStartCell, CableHeight + 0.02f);
            Vector3 to = GridUtility.CellToWorldCenter(currentCell, CableHeight + 0.02f);
            GameObject wire = BuildVisuals.CreateStrip(from, to, CableWidth * 1.7f,
                new Color(0.2f, 0.95f, 1f), null, false);
            wire.name = "PowerWirePreview";
            placementPreview.Add(wire);
        }

        private static bool IsGeneratorTowerPair(PowerNodeRuntime first, PowerNodeRuntime second)
        {
            if (first == null || second == null) return false;
            return (first.Kind == PowerNodeKind.Generator && second.Kind == PowerNodeKind.TransmissionTower)
                || (first.Kind == PowerNodeKind.TransmissionTower && second.Kind == PowerNodeKind.Generator);
        }

        private void ShowTowerRange(Vector2Int centerCell)
        {
            ClearPlacementPreview();
            CreateTowerRange(centerCell, placementPreview, new Color(0.35f, 0.9f, 1f));
        }

        private void SelectPowerRangeAt(Vector2Int cell)
        {
            if (powerGrid != null && powerGrid.IsCoreCell(cell))
            {
                if (isCoreRangeSelected)
                {
                    ClearCoreRangePreview();
                    LastMessage = "코어 전력 범위 표시 종료";
                    return;
                }

                ClearTowerSelectionPreview();
                ShowCoreRangePreview();
                LastMessage = $"코어 선택 · 공급 범위 {PowerGridSystem.CoreRangeSize}x{PowerGridSystem.CoreRangeSize}";
                return;
            }

            if (powerGrid == null
                || !powerGrid.TryGetNode(cell, out PowerNodeRuntime node)
                || node.Kind != PowerNodeKind.TransmissionTower)
            {
                ClearPowerRangeSelection();
                return;
            }

            if (selectedTowerId == node.Id && towerSelectionPreview.Count > 0)
            {
                ClearTowerSelectionPreview();
                LastMessage = "송전탑 범위 표시 종료";
                return;
            }

            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
            selectedTowerId = node.Id;
            CreateTowerRange(cell, towerSelectionPreview, new Color(0.72f, 0.35f, 1f));
            LastMessage = $"송전탑 선택: {cell} · 공급 범위 15x15";
        }

        private void CreateTowerRange(Vector2Int centerCell, List<GameObject> target, Color color)
        {
            float minX = centerCell.x - TowerRangeRadius;
            float maxX = centerCell.x + TowerRangeRadius + 1f;
            float minZ = centerCell.y - TowerRangeRadius;
            float maxZ = centerCell.y + TowerRangeRadius + 1f;
            const float height = 0.09f;
            const float width = 0.075f;

            CreatePreviewStrip(new Vector3(minX, height, minZ), new Vector3(maxX, height, minZ), width, color, target);
            CreatePreviewStrip(new Vector3(maxX, height, minZ), new Vector3(maxX, height, maxZ), width, color, target);
            CreatePreviewStrip(new Vector3(maxX, height, maxZ), new Vector3(minX, height, maxZ), width, color, target);
            CreatePreviewStrip(new Vector3(minX, height, maxZ), new Vector3(minX, height, minZ), width, color, target);
        }

        private void ShowCoreRangePreview()
        {
            if (powerGrid == null || !powerGrid.TryGetCorePowerCenter(out Vector2 center))
            {
                ClearCoreRangePreview();
                return;
            }

            ClearCoreRangePreview();
            isCoreRangeSelected = true;

            float halfRange = PowerGridSystem.CoreRangeSize * 0.5f;
            // 2x2 코어 중심을 기준으로 12x12 영역의 네 변을 그리드 경계에 맞춘다.
            float minX = Mathf.Floor(center.x - halfRange);
            float maxX = minX + PowerGridSystem.CoreRangeSize;
            float minZ = Mathf.Floor(center.y - halfRange);
            float maxZ = minZ + PowerGridSystem.CoreRangeSize;
            const float height = 0.085f;
            const float width = 0.09f;
            Color color = new Color(0.18f, 1f, 0.52f, 0.9f);

            CreatePreviewStrip(new Vector3(minX, height, minZ), new Vector3(maxX, height, minZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(maxX, height, minZ), new Vector3(maxX, height, maxZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(maxX, height, maxZ), new Vector3(minX, height, maxZ), width, color, coreRangePreview);
            CreatePreviewStrip(new Vector3(minX, height, maxZ), new Vector3(minX, height, minZ), width, color, coreRangePreview);
            for (int i = 0; i < coreRangePreview.Count; i++) coreRangePreview[i].name = "CorePowerRange";
        }

        private void CreatePreviewStrip(Vector3 from, Vector3 to, float width, Color color, List<GameObject> target)
        {
            GameObject strip = BuildVisuals.CreateStrip(from, to, width, color, null, false);
            strip.name = "PowerPlacementPreview";
            target.Add(strip);
        }

        private void CancelPlacementPreview()
        {
            isCableDragging = false;
            isDraggingNodePlacement = false;
            hasPendingNodePlacement = false;
            cableStartNode = null;
            cableDragPath.Clear();
            if (nodePlacementGhost != null) Destroy(nodePlacementGhost);
            nodePlacementGhost = null;
            ClearPlacementPreview();
        }

        private void ClearPlacementPreview()
        {
            for (int i = 0; i < placementPreview.Count; i++)
            {
                if (placementPreview[i] != null) Destroy(placementPreview[i]);
            }
            placementPreview.Clear();
        }

        private void ClearTowerSelectionPreview()
        {
            for (int i = 0; i < towerSelectionPreview.Count; i++)
            {
                if (towerSelectionPreview[i] != null) Destroy(towerSelectionPreview[i]);
            }
            towerSelectionPreview.Clear();
            selectedTowerId = -1;
        }

        private void ClearCoreRangePreview()
        {
            for (int i = 0; i < coreRangePreview.Count; i++)
            {
                if (coreRangePreview[i] != null) Destroy(coreRangePreview[i]);
            }
            coreRangePreview.Clear();
            isCoreRangeSelected = false;
        }

        private void ClearPowerRangeSelection()
        {
            ClearTowerSelectionPreview();
            ClearCoreRangePreview();
        }

        private void RestoreBuildRouter()
        {
            if (buildRouter != null) buildRouter.enabled = true;
        }

        private static bool TryGetPointerState(out Vector2 position, out int? pointerId,
            out bool pressed, out bool held, out bool released)
        {
            if (Touchscreen.current != null)
            {
                var touches = Touchscreen.current.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    bool touchPressed = touches[i].press.wasPressedThisFrame;
                    bool touchHeld = touches[i].press.isPressed;
                    bool touchReleased = touches[i].press.wasReleasedThisFrame;
                    if (!touchPressed && !touchHeld && !touchReleased) continue;
                    position = touches[i].position.ReadValue();
                    pointerId = touches[i].touchId.ReadValue();
                    pressed = touchPressed;
                    held = touchHeld;
                    released = touchReleased;
                    return true;
                }
            }

            if (Mouse.current != null)
            {
                position = Mouse.current.position.ReadValue();
                pointerId = null;
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                held = Mouse.current.leftButton.isPressed;
                released = Mouse.current.leftButton.wasReleasedThisFrame;
                return true;
            }

            position = default;
            pointerId = null;
            pressed = false;
            held = false;
            released = false;
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
