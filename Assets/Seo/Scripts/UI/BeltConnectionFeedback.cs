using System.Collections.Generic;
using System.Reflection;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // BeltDragTool의 내부 상태를 읽기만 하는 UI 어댑터. 벨트 연결 규칙은 변경하지 않고
    // 현재 시작점/도착점이 왜 유효하거나 무효한지만 플레이어에게 설명한다.
    public sealed class BeltConnectionFeedback : MonoBehaviour
    {
        private enum PortRole { None, Source, Target }

        private static readonly FieldInfo PathField = typeof(BeltDragTool).GetField("path", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo PreviewStripsField = typeof(BeltDragTool).GetField("previewStrips", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DraggingField = typeof(BeltDragTool).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DriverField = typeof(BeltDragTool).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        private BuildInputRouter router;
        private BeltDragTool beltTool;
        private SimulationDriver driver;
        private Camera targetCamera;
        private GameObject panelRoot;
        private Text messageText;
        private EndpointBadge startBadge;
        private EndpointBadge endBadge;
        private bool wasDragging;
        private bool lastPathValid;
        private string releaseMessage;
        private Color releaseColor;
        private float releaseMessageUntil;
        private float nextDiscovery;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<BeltConnectionFeedback>() != null) return;
            new GameObject("[Seo] Belt Connection Feedback").AddComponent<BeltConnectionFeedback>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscovery)
            {
                Discover();
                nextDiscovery = Time.unscaledTime + 0.5f;
            }

            if (router == null || beltTool == null || driver == null || driver.World == null || !EnsurePanel()) return;

            bool beltMode = router.CurrentMode == BuildInputRouter.Mode.Belt;
            panelRoot.SetActive(beltMode);
            if (!beltMode)
            {
                HideEndpointBadges();
                wasDragging = false;
                return;
            }

            bool dragging = DraggingField != null && (bool)DraggingField.GetValue(beltTool);
            var path = PathField?.GetValue(beltTool) as List<Vector2Int>;

            if (!dragging)
            {
                if (wasDragging)
                {
                    releaseMessage = lastPathValid
                        ? "벨트 연결 완료"
                        : "연결되지 않음 · 빨간 안내를 확인하세요";
                    releaseColor = lastPathValid ? SeoUITheme.Current.Success : SeoUITheme.Current.Danger;
                    releaseMessageUntil = Time.unscaledTime + 1.6f;
                }

                wasDragging = false;
                lastPathValid = false;
                HideEndpointBadges();
                SetMessage(Time.unscaledTime < releaseMessageUntil
                    ? releaseMessage
                    : "벨트 · 기계 출력 포트에서 입력 포트까지 드래그", Time.unscaledTime < releaseMessageUntil
                        ? releaseColor
                        : SeoUITheme.Current.Primary);
                return;
            }

            wasDragging = true;
            ValidatePath(path);
        }

        private void Discover()
        {
            if (router == null) router = FindFirstObjectByType<BuildInputRouter>();
            if (beltTool == null) beltTool = FindFirstObjectByType<BeltDragTool>();
            if (driver == null && beltTool != null) driver = DriverField?.GetValue(beltTool) as SimulationDriver;
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        private bool EnsurePanel()
        {
            if (panelRoot != null) return true;
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;

            Transform parent = canvasObject.transform.Find("SafeArea") ?? canvasObject.transform;
            var panel = SeoUIFactory.CreatePanel(parent, "SeoBeltFeedback", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 306f), new Vector2(820f, 72f),
                new Color(0.02f, 0.08f, 0.12f, 0.97f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0f);
            panelRoot = panel.gameObject;
            messageText = SeoUIFactory.CreateText(panel.transform, "Message", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            messageText.rectTransform.offsetMin = new Vector2(24f, 8f);
            messageText.rectTransform.offsetMax = new Vector2(-24f, -8f);
            panelRoot.SetActive(false);
            return true;
        }

        private void ValidatePath(List<Vector2Int> path)
        {
            lastPathValid = false;
            if (path == null || path.Count == 0)
            {
                HideEndpointBadges();
                SetMessage("출력 포트 또는 기존 벨트에서 드래그를 시작하세요", SeoUITheme.Current.Warning);
                return;
            }

            var grid = driver.World.Grid;
            Vector2Int startCell = path[0];
            bool startOccupied = grid.IsOccupied(startCell);
            ShowBadge(ref startBadge, startCell, "시작", startOccupied ? SeoUITheme.Current.Primary : SeoUITheme.Current.Danger);

            if (!startOccupied)
            {
                HideBadge(ref endBadge);
                SetMessage("연결 불가 · 빈 공간에서는 벨트를 시작할 수 없습니다", SeoUITheme.Current.Danger);
                return;
            }

            if (path.Count < 2)
            {
                HideBadge(ref endBadge);
                SetMessage("시작점 선택됨 · 연결할 방향으로 한 칸 이상 드래그하세요", SeoUITheme.Current.Primary);
                return;
            }

            grid.TryGetOccupant(startCell, out var startOccupant);
            PortRole startRole = ResolveRole(startOccupant, path[1], true, out bool startFixed);
            UpdateBadge(startBadge, startRole == PortRole.None ? "연결 불가" : "시작",
                startRole == PortRole.None ? SeoUITheme.Current.Danger : RoleColor(startRole));

            if (startRole == PortRole.None)
            {
                SetMessage(InvalidEndpointReason(startOccupant, true), SeoUITheme.Current.Danger);
                HideBadge(ref endBadge);
                return;
            }

            for (int i = 1; i < path.Count - 1; i++)
            {
                if (!grid.IsOccupied(path[i])) continue;
                ShowBadge(ref endBadge, path[i], "경로 충돌", SeoUITheme.Current.Danger);
                SetMessage("연결 불가 · 벨트 경로에 건물 또는 기존 벨트가 있습니다", SeoUITheme.Current.Danger);
                return;
            }

            Vector2Int endCell = path[path.Count - 1];
            bool endOccupied = grid.IsOccupied(endCell);
            PortRole endRole = PortRole.None;
            bool endFixed = false;
            CellOccupant endOccupant = default;

            if (endOccupied)
            {
                grid.TryGetOccupant(endCell, out endOccupant);
                endRole = ResolveRole(endOccupant, path[path.Count - 2], false, out endFixed);
                ShowBadge(ref endBadge, endCell,
                    endRole == PortRole.None ? "연결 불가" : "도착",
                    endRole == PortRole.None ? SeoUITheme.Current.Danger : RoleColor(endRole));

                if (endRole == PortRole.None)
                {
                    SetMessage(InvalidEndpointReason(endOccupant, false), SeoUITheme.Current.Danger);
                    return;
                }

                if (startRole == endRole)
                {
                    if (!startFixed && endFixed) startRole = Opposite(endRole);
                    else if (startFixed && !endFixed) endRole = Opposite(startRole);
                    else
                    {
                        SetMessage(startRole == PortRole.Source
                            ? "연결 불가 · 출력 포트끼리는 연결할 수 없습니다"
                            : "연결 불가 · 입력 포트끼리는 연결할 수 없습니다", SeoUITheme.Current.Danger);
                        UpdateBadge(startBadge, "연결 불가", SeoUITheme.Current.Danger);
                        UpdateBadge(endBadge, "연결 불가", SeoUITheme.Current.Danger);
                        return;
                    }
                }
            }
            else
            {
                ShowBadge(ref endBadge, endCell, "도착", SeoUITheme.Current.Warning);
            }

            if (startOccupant.Type == CellOccupantType.Belt && startRole == PortRole.Source)
            {
                var segment = driver.World.Segments[startOccupant.InstanceIndex];
                if (segment.NextSegmentId.HasValue || segment.TargetProcessorId.HasValue)
                {
                    SetMessage("연결 불가 · 이미 출력이 연결된 벨트입니다", SeoUITheme.Current.Danger);
                    UpdateBadge(startBadge, "이미 연결됨", SeoUITheme.Current.Danger);
                    return;
                }
            }

            if (!endOccupied)
            {
                SetMessage(startRole == PortRole.Source
                    ? $"출력에서 경로 생성 중 · 입력 포트까지 드래그 ({path.Count - 1}칸)"
                    : "현재 시작점은 입력 포트입니다 · 반대쪽 출력 포트까지 연결하세요",
                    startRole == PortRole.Source ? SeoUITheme.Current.Warning : SeoUITheme.Current.Danger);
                return;
            }

            bool rolesValid = (startRole == PortRole.Source && endRole == PortRole.Target)
                || (startRole == PortRole.Target && endRole == PortRole.Source);
            if (!rolesValid)
            {
                SetMessage("연결 불가 · 출력과 입력 포트를 확인하세요", SeoUITheme.Current.Danger);
                return;
            }

            CellOccupant target = startRole == PortRole.Target ? startOccupant : endOccupant;
            PortRole targetRole = PortRole.Target;
            lastPathValid = true;
            SetMessage($"연결 가능 · {EndpointName(startOccupant)} → {EndpointName(endOccupant)} · {path.Count - 1}칸"
                + ConnectionCountSuffix(target, targetRole), SeoUITheme.Current.Success);
            UpdateBadge(startBadge, startRole == PortRole.Source ? "시작" : "도착", RoleColor(startRole));
            UpdateBadge(endBadge, endRole == PortRole.Target ? "도착" : "시작", RoleColor(endRole));
        }

        private PortRole ResolveRole(CellOccupant occupant, Vector2Int touchingCell, bool isStart, out bool fixedRole)
        {
            fixedRole = false;
            if (occupant.Type == CellOccupantType.Miner)
            {
                fixedRole = true;
                return PortRole.None;
            }

            if (occupant.Type == CellOccupantType.Belt)
            {
                if (isStart) return PortRole.Source;
                var segment = driver.World.Segments[occupant.InstanceIndex];
                return MachineGhostTool.IsChainStart(driver.World.Segments, segment) ? PortRole.Target : PortRole.None;
            }

            if (occupant.InstanceIndex < 0 || occupant.InstanceIndex >= driver.World.Processors.Count) return PortRole.None;
            var processor = driver.World.Processors[occupant.InstanceIndex];
            if (processor == null) return PortRole.None;

            if (processor.RoutingRole != RoutingRole.None)
            {
                fixedRole = true;
                Vector2Int direction = touchingCell - processor.Anchor;
                bool input = processor.RoutingRole == RoutingRole.Splitter
                    ? direction == -processor.Facing
                    : direction != processor.Facing;
                return input ? PortRole.Target : PortRole.Source;
            }

            if (processor.UniversalPorts) return isStart ? PortRole.Source : PortRole.Target;

            fixedRole = true;
            if (GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true).Contains(touchingCell))
                return PortRole.Source;
            if (GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false).Contains(touchingCell))
                return PortRole.Target;
            return PortRole.None;
        }

        private string InvalidEndpointReason(CellOccupant occupant, bool start)
        {
            if (occupant.Type == CellOccupantType.Miner)
                return "연결 불가 · 채굴기는 벨트 포트가 없고 코어로 자동 전송합니다";
            if (occupant.Type == CellOccupantType.Belt)
                return "연결 불가 · 해당 벨트는 이미 상류가 연결되어 있습니다";
            return start
                ? "연결 불가 · 기계의 입력/출력 포트가 아닌 면입니다"
                : "연결 불가 · 도착 지점이 유효한 입력/출력 포트가 아닙니다";
        }

        private string EndpointName(CellOccupant occupant)
        {
            if (occupant.Type == CellOccupantType.Belt) return "기존 벨트";
            if (occupant.Type == CellOccupantType.Miner) return "채굴기";
            if (occupant.InstanceIndex < 0 || occupant.InstanceIndex >= driver.World.Processors.Count) return "기계";
            var processor = driver.World.Processors[occupant.InstanceIndex];
            if (processor == null) return "기계";
            string key = driver.World.Database.Machines[processor.MachineId].Key;
            return MachineInfoPresenter.GetMachineDisplayName(key);
        }

        private string ConnectionCountSuffix(CellOccupant occupant, PortRole role)
        {
            if (occupant.Type != CellOccupantType.Processor) return string.Empty;
            var processor = driver.World.Processors[occupant.InstanceIndex];
            if (processor == null) return string.Empty;

            int inputs = 0;
            int outputs = 0;
            for (int i = 0; i < driver.World.Segments.Count; i++)
            {
                var segment = driver.World.Segments[i];
                if (segment == null) continue;
                if (segment.TargetProcessorId == occupant.InstanceIndex) inputs++;
                if (segment.SourceProcessorId == occupant.InstanceIndex) outputs++;
            }

            string key = driver.World.Database.Machines[processor.MachineId].Key;
            int maxInputs;
            int maxOutputs;
            if (processor.UniversalPorts) maxInputs = maxOutputs = 4;
            else if (processor.RoutingRole == RoutingRole.Splitter) { maxInputs = 1; maxOutputs = 3; }
            else if (processor.RoutingRole == RoutingRole.Merger) { maxInputs = 3; maxOutputs = 1; }
            else
            {
                maxInputs = Mathf.Max(1, GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false).Count);
                maxOutputs = key == "Synthesizer" ? 1 : Mathf.Max(1,
                    GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true).Count);
            }

            return role == PortRole.Target
                ? $" · 입력 {Mathf.Min(inputs + 1, maxInputs)}/{maxInputs}"
                : $" · 출력 {Mathf.Min(outputs + 1, maxOutputs)}/{maxOutputs}";
        }

        private void SetMessage(string message, Color color)
        {
            if (messageText == null) return;
            messageText.text = message;
            messageText.color = color;
            TintPreview(color);
        }

        private void TintPreview(Color color)
        {
            if (beltTool == null || PreviewStripsField == null) return;
            var strips = PreviewStripsField.GetValue(beltTool) as List<GameObject>;
            if (strips == null) return;
            var previewColor = new Color(color.r, color.g, color.b, 0.58f);
            for (int i = 0; i < strips.Count; i++)
            {
                if (strips[i] != null) BuildVisuals.Colorize(strips[i], previewColor);
            }
        }

        private static PortRole Opposite(PortRole role)
        {
            return role == PortRole.Source ? PortRole.Target : role == PortRole.Target ? PortRole.Source : PortRole.None;
        }

        private static Color RoleColor(PortRole role)
        {
            return role == PortRole.Source ? new Color(1f, 0.58f, 0.12f) : new Color(0.2f, 0.72f, 1f);
        }

        private void ShowBadge(ref EndpointBadge badge, Vector2Int cell, string label, Color color)
        {
            if (badge == null) badge = EndpointBadge.Create();
            badge.Root.SetActive(true);
            badge.Set(label, color);
            // 기계의 기존 IN/OUT 배지는 포트 높이에 있으므로, 연결점 배지는 그보다 위에 띄워
            // 두 텍스트가 같은 화면 위치에 투영되지 않게 한다.
            badge.Root.transform.position = GridUtility.CellToWorldCenter(cell, 2.15f);
            if (targetCamera != null) badge.Root.transform.rotation = targetCamera.transform.rotation;
        }

        private static void UpdateBadge(EndpointBadge badge, string label, Color color)
        {
            badge?.Set(label, color);
        }

        private void HideEndpointBadges()
        {
            HideBadge(ref startBadge);
            HideBadge(ref endBadge);
        }

        private static void HideBadge(ref EndpointBadge badge)
        {
            if (badge != null) badge.Root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (startBadge != null) Destroy(startBadge.Root);
            if (endBadge != null) Destroy(endBadge.Root);
        }

        private sealed class EndpointBadge
        {
            public readonly GameObject Root;
            private readonly Image background;
            private readonly Text label;

            private EndpointBadge(GameObject root, Image background, Text label)
            {
                Root = root;
                this.background = background;
                this.label = label;
            }

            public static EndpointBadge Create()
            {
                var root = new GameObject("BeltEndpointBadge", typeof(RectTransform), typeof(Canvas));
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 45;
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(92f, 28f);
                root.transform.localScale = Vector3.one * 0.0048f;

                var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
                backgroundObject.transform.SetParent(root.transform, false);
                var backgroundRect = backgroundObject.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;

                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(root.transform, false);
                var labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var text = labelObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 14;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.raycastTarget = false;
                return new EndpointBadge(root, backgroundObject.GetComponent<Image>(), text);
            }

            public void Set(string value, Color color)
            {
                label.text = value;
                background.color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.96f);
            }
        }
    }
}
