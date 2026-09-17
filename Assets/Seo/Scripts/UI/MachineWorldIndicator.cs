using System.Collections.Generic;
using Choi.SaveLoad;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 단순 색상 박스만으로는 기계 종류와 포트를 알아보기 어려워서, 배치된 기계 위에 이름과
    // 실제 연결 셀 기준 IN/OUT 표식을 그린다. 게임 데이터에는 손대지 않는 월드 UI 전용 뷰다.
    public sealed class MachineWorldIndicator : MonoBehaviour
    {
        private readonly List<WorldBadge> portBadges = new List<WorldBadge>();

        private MachineInstanceKind kind;
        private int instanceIndex;
        private SimulationDriver driver;
        private Camera targetCamera;
        private WorldBadge nameBadge;
        private WorldBadge statusBadge;
        private PowerGridSystem powerGrid;
        private string machineKey;
        private float nextStatusUpdate;

        private static readonly Vector2Int[] FourDirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
        };

        public bool IsInitialized => driver != null;

        public void Initialize(MachineInstanceKind instanceKind, int index, SimulationDriver simulationDriver)
        {
            kind = instanceKind;
            instanceIndex = index;
            driver = simulationDriver;
            targetCamera = Camera.main;
            powerGrid = FindFirstObjectByType<PowerGridSystem>();
            Rebuild();
        }

        private void LateUpdate()
        {
            if (driver == null || driver.World == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (Time.unscaledTime >= nextStatusUpdate)
            {
                UpdateStatus();
                nextStatusUpdate = Time.unscaledTime + 0.2f;
            }
            UpdatePositions();
        }

        private void OnDestroy()
        {
            DestroyBadge(nameBadge);
            DestroyBadge(statusBadge);
            for (int i = 0; i < portBadges.Count; i++) DestroyBadge(portBadges[i]);
        }

        private void Rebuild()
        {
            for (int i = 0; i < portBadges.Count; i++) DestroyBadge(portBadges[i]);
            portBadges.Clear();
            DestroyBadge(nameBadge);
            DestroyBadge(statusBadge);

            if (driver == null || driver.World == null) return;

            int machineId;
            if (kind == MachineInstanceKind.Miner)
            {
                if (instanceIndex < 0 || instanceIndex >= driver.World.Miners.Count || driver.World.Miners[instanceIndex] == null) return;
                machineId = driver.World.Miners[instanceIndex].MachineId;
            }
            else
            {
                if (instanceIndex < 0 || instanceIndex >= driver.World.Processors.Count || driver.World.Processors[instanceIndex] == null) return;
                machineId = driver.World.Processors[instanceIndex].MachineId;
            }

            string title = MachineInfoPresenter.GetMachineDisplayName(driver.World, machineId);
            machineKey = driver.World.Database.Machines[machineId].Key;
            nameBadge = CreateBadge(title, MachineInfoPresenter.GetMachineColor(machineKey), new Vector2(150f, 34f), 18);
            statusBadge = CreateBadge("상태 확인 중", SeoUITheme.Current.Muted, new Vector2(130f, 28f), 14);
            UpdateStatus();

            if (kind == MachineInstanceKind.Miner)
            {
                portBadges.Add(CreateBadge("AUTO → CORE", new Color(0.95f, 0.72f, 0.15f), new Vector2(120f, 30f), 14));
                return;
            }

            var processor = driver.World.Processors[instanceIndex];
            if (processor.UniversalPorts)
            {
                for (int i = 0; i < 4; i++)
                    portBadges.Add(CreateBadge("↔", new Color(0.1f, 0.78f, 1f), new Vector2(52f, 48f), 24, true));
                return;
            }

            if (processor.RoutingRole != RoutingRole.None)
            {
                for (int i = 0; i < FourDirs.Length; i++)
                {
                    bool input = processor.RoutingRole == RoutingRole.Splitter
                        ? FourDirs[i] == -processor.Facing
                        : FourDirs[i] != processor.Facing;
                    Vector2Int flowDirection = input ? -FourDirs[i] : FourDirs[i];
                    portBadges.Add(CreateBadge(DirectionArrow(flowDirection),
                        input ? new Color(0.05f, 0.78f, 1f) : new Color(1f, 0.48f, 0.05f),
                        new Vector2(52f, 48f), 24, true));
                }
                return;
            }

            var inputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false);
            var outputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true);
            string flowArrow = DirectionArrow(processor.Facing);
            for (int i = 0; i < inputCells.Count; i++)
                portBadges.Add(CreateBadge(flowArrow, new Color(0.05f, 0.78f, 1f), new Vector2(52f, 48f), 24, true));
            int visibleOutputs = machineKey == "Synthesizer" ? Mathf.Min(1, outputCells.Count) : outputCells.Count;
            for (int i = 0; i < visibleOutputs; i++)
                portBadges.Add(CreateBadge(flowArrow, new Color(1f, 0.48f, 0.05f), new Vector2(52f, 48f), 24, true));
        }

        private static string DirectionArrow(Vector2Int direction)
        {
            if (direction == Vector2Int.right) return "▶";
            if (direction == Vector2Int.left) return "◀";
            if (direction == Vector2Int.up) return "▲";
            return "▼";
        }

        private void UpdatePositions()
        {
            if (nameBadge == null) return;
            var bounds = CalculateBounds();
            SetBadgeTransform(statusBadge, new Vector3(bounds.center.x, bounds.max.y + 0.6f, bounds.center.z), 0.0055f);
            SetBadgeTransform(nameBadge, new Vector3(bounds.center.x, bounds.max.y + 0.3f, bounds.center.z), 0.0065f);

            if (kind == MachineInstanceKind.Miner)
            {
                if (portBadges.Count > 0) SetBadgeTransform(portBadges[0], new Vector3(bounds.center.x, bounds.max.y + 0.02f, bounds.center.z), 0.005f);
                return;
            }

            if (instanceIndex < 0 || instanceIndex >= driver.World.Processors.Count) return;
            var processor = driver.World.Processors[instanceIndex];
            if (processor == null) return;

            if (processor.UniversalPorts)
            {
                Vector3 center = GridUtility.GetFootprintCenter(processor.Anchor, processor.Footprint, bounds.max.y + 0.12f);
                float halfX = processor.Footprint.x * GridUtility.CellSize * 0.5f + 0.18f;
                float halfZ = processor.Footprint.y * GridUtility.CellSize * 0.5f + 0.18f;
                portBadges[0].SetVisible(!IsUniversalSideConnected(processor, Vector2Int.left));
                portBadges[1].SetVisible(!IsUniversalSideConnected(processor, Vector2Int.right));
                portBadges[2].SetVisible(!IsUniversalSideConnected(processor, Vector2Int.down));
                portBadges[3].SetVisible(!IsUniversalSideConnected(processor, Vector2Int.up));
                SetBadgeTransform(portBadges[0], center + Vector3.left * halfX, 0.0065f);
                SetBadgeTransform(portBadges[1], center + Vector3.right * halfX, 0.0065f);
                SetBadgeTransform(portBadges[2], center + Vector3.back * halfZ, 0.0065f);
                SetBadgeTransform(portBadges[3], center + Vector3.forward * halfZ, 0.0065f);
                return;
            }

            if (processor.RoutingRole != RoutingRole.None)
            {
                for (int i = 0; i < FourDirs.Length && i < portBadges.Count; i++)
                {
                    Vector2Int cell = processor.Anchor + FourDirs[i];
                    bool input = processor.RoutingRole == RoutingRole.Splitter
                        ? FourDirs[i] == -processor.Facing
                        : FourDirs[i] != processor.Facing;
                    portBadges[i].SetVisible(!IsPortConnected(cell, input));
                    SetBadgeTransform(portBadges[i], GridUtility.CellToWorldCenter(cell, bounds.max.y + 0.12f), 0.0065f);
                }
                return;
            }

            var inputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false);
            var outputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true);
            int badgeIndex = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                portBadges[badgeIndex].SetVisible(!IsPortConnected(inputs[i], true));
                SetBadgeTransform(portBadges[badgeIndex++], GridUtility.CellToWorldCenter(inputs[i], bounds.max.y + 0.12f), 0.0065f);
            }
            int visibleOutputs = machineKey == "Synthesizer" ? Mathf.Min(1, outputs.Count) : outputs.Count;
            for (int i = 0; i < visibleOutputs; i++)
            {
                portBadges[badgeIndex].SetVisible(!IsPortConnected(outputs[i], false));
                Vector3 position = machineKey == "Synthesizer"
                    ? GridUtility.GetFootprintCenter(processor.Anchor, processor.Footprint, bounds.max.y + 0.12f)
                        + new Vector3(processor.Facing.x, 0f, processor.Facing.y) * (GridUtility.CellSize * 1.5f)
                    : GridUtility.CellToWorldCenter(outputs[i], bounds.max.y + 0.12f);
                SetBadgeTransform(portBadges[badgeIndex++], position, 0.0065f);
            }
        }

        private bool IsPortConnected(Vector2Int cell, bool input)
        {
            if (driver == null || driver.World == null) return false;
            if (!driver.World.Grid.TryGetOccupant(cell, out var occupant)
                || occupant.Type != CellOccupantType.Belt
                || occupant.InstanceIndex < 0
                || occupant.InstanceIndex >= driver.World.Segments.Count)
                return false;

            var segment = driver.World.Segments[occupant.InstanceIndex];
            if (segment == null) return false;
            return input
                ? segment.TargetProcessorId == instanceIndex
                : segment.SourceProcessorId == instanceIndex;
        }

        private bool IsUniversalSideConnected(ProcessorInstance processor, Vector2Int side)
        {
            if (driver == null || driver.World == null) return false;
            int count = side.x != 0 ? processor.Footprint.y : processor.Footprint.x;
            for (int i = 0; i < count; i++)
            {
                Vector2Int cell;
                if (side == Vector2Int.left)
                    cell = new Vector2Int(processor.Anchor.x - 1, processor.Anchor.y + i);
                else if (side == Vector2Int.right)
                    cell = new Vector2Int(processor.Anchor.x + processor.Footprint.x, processor.Anchor.y + i);
                else if (side == Vector2Int.down)
                    cell = new Vector2Int(processor.Anchor.x + i, processor.Anchor.y - 1);
                else
                    cell = new Vector2Int(processor.Anchor.x + i, processor.Anchor.y + processor.Footprint.y);

                if (!driver.World.Grid.TryGetOccupant(cell, out var occupant)
                    || occupant.Type != CellOccupantType.Belt
                    || occupant.InstanceIndex < 0
                    || occupant.InstanceIndex >= driver.World.Segments.Count)
                    continue;

                var segment = driver.World.Segments[occupant.InstanceIndex];
                if (segment != null && (segment.SourceProcessorId == instanceIndex
                    || segment.TargetProcessorId == instanceIndex))
                    return true;
            }
            return false;
        }

        private void UpdateStatus()
        {
            if (statusBadge == null || driver == null || driver.World == null) return;
            if (powerGrid == null) powerGrid = FindFirstObjectByType<PowerGridSystem>();

            if (kind == MachineInstanceKind.Miner)
            {
                if (instanceIndex < 0 || instanceIndex >= driver.World.Miners.Count) return;
                var miner = driver.World.Miners[instanceIndex];
                if (miner == null) return;

                if (powerGrid != null && !powerGrid.IsMachinePowered(CellOccupantType.Miner, instanceIndex))
                    statusBadge.SetContent("전력 부족", SeoUITheme.Current.Danger);
                else if (miner.BufferedOutput > 0)
                    statusBadge.SetContent("전송 대기", SeoUITheme.Current.Warning);
                else
                    statusBadge.SetContent("채굴 중", SeoUITheme.Current.Success);
                return;
            }

            if (instanceIndex < 0 || instanceIndex >= driver.World.Processors.Count) return;
            var processor = driver.World.Processors[instanceIndex];
            if (processor == null) return;

            if (processor.UniversalPorts)
            {
                statusBadge.SetContent("중앙 저장소", SeoUITheme.Current.Primary);
                return;
            }

            // 분류기/합류기는 전력을 쓰지 않는 물류 설비이므로 전력 표시보다 먼저 판정한다.
            if (processor.RoutingRole != RoutingRole.None)
            {
                statusBadge.SetContent("물류 가동", SeoUITheme.Current.Success);
                return;
            }

            if (powerGrid != null && !powerGrid.IsMachinePowered(CellOccupantType.Processor, instanceIndex))
            {
                statusBadge.SetContent("전력 부족", SeoUITheme.Current.Danger);
                return;
            }

            var db = driver.World.Database;
            if (processor.RecipeId < 0 || processor.RecipeId >= db.Recipes.Count)
            {
                statusBadge.SetContent("레시피 없음", SeoUITheme.Current.Muted);
                return;
            }

            var recipe = db.Recipes[processor.RecipeId];
            if (processor.IsProcessing)
            {
                statusBadge.SetContent("가동 중", SeoUITheme.Current.Success);
                return;
            }

            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                var output = recipe.Outputs[i];
                if (processor.OutputBuffer[output.ResourceId] >= processor.Capacity)
                {
                    statusBadge.SetContent("출력 막힘", new Color(1f, 0.42f, 0.08f));
                    return;
                }
            }

            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var input = recipe.Inputs[i];
                if (processor.InputBuffer[input.ResourceId] < input.Amount)
                {
                    statusBadge.SetContent("입력 부족", SeoUITheme.Current.Warning);
                    return;
                }
            }

            statusBadge.SetContent("가동 준비", SeoUITheme.Current.Primary);
        }

        private Bounds CalculateBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            Bounds bounds = default;
            bool has = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                // 연기 같은 연출용 파티클 렌더러는 뺀다 — 안 그러면 연기가 위로 퍼질수록 그
                // 렌더러의 바운드도 매 프레임 같이 커져서, 그 위에 얹는 이름표가 덩달아 움직인다.
                if (renderers[i] is ParticleSystemRenderer) continue;
                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            return has ? bounds : new Bounds(transform.position, Vector3.one);
        }

        private void SetBadgeTransform(WorldBadge badge, Vector3 position, float scale)
        {
            if (badge == null || badge.Root == null) return;
            badge.Root.transform.position = position;
            if (targetCamera != null) badge.Root.transform.rotation = targetCamera.transform.rotation;
            badge.Root.transform.localScale = Vector3.one * scale;
        }

        private static WorldBadge CreateBadge(string label, Color color, Vector2 size, int fontSize,
            bool highContrast = false)
        {
            var root = new GameObject("WorldUI_" + label, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            var rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            var backgroundGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundGO.transform.SetParent(root.transform, false);
            var backgroundRt = backgroundGO.GetComponent<RectTransform>();
            backgroundRt.anchorMin = Vector2.zero;
            backgroundRt.anchorMax = Vector2.one;
            backgroundRt.offsetMin = Vector2.zero;
            backgroundRt.offsetMax = Vector2.zero;
            var background = backgroundGO.GetComponent<Image>();
            float colorStrength = highContrast ? 0.78f : 0.45f;
            background.color = new Color(color.r * colorStrength, color.g * colorStrength,
                color.b * colorStrength, highContrast ? 0.99f : 0.94f);

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(root.transform, false);
            var textRt = textGO.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGO.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
            if (highContrast)
            {
                var outline = textGO.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
            }

            return new WorldBadge(root, background, text);
        }

        private static void DestroyBadge(WorldBadge badge)
        {
            if (badge != null && badge.Root != null) Destroy(badge.Root);
        }

        private sealed class WorldBadge
        {
            public readonly GameObject Root;
            private readonly Image background;
            private readonly Text label;

            public WorldBadge(GameObject root, Image background, Text label)
            {
                Root = root;
                this.background = background;
                this.label = label;
            }

            public void SetVisible(bool visible)
            {
                if (Root != null && Root.activeSelf != visible) Root.SetActive(visible);
            }

            public void SetContent(string value, Color color)
            {
                if (label != null) label.text = value;
                if (background != null)
                    background.color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.96f);
            }
        }
    }
}
