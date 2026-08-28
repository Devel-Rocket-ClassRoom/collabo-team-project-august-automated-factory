using System.Collections.Generic;
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

        public bool IsInitialized => driver != null;

        public void Initialize(MachineInstanceKind instanceKind, int index, SimulationDriver simulationDriver)
        {
            kind = instanceKind;
            instanceIndex = index;
            driver = simulationDriver;
            targetCamera = Camera.main;
            Rebuild();
        }

        private void LateUpdate()
        {
            if (driver == null || driver.World == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            UpdatePositions();
        }

        private void OnDestroy()
        {
            DestroyBadge(nameBadge);
            for (int i = 0; i < portBadges.Count; i++) DestroyBadge(portBadges[i]);
        }

        private void Rebuild()
        {
            for (int i = 0; i < portBadges.Count; i++) DestroyBadge(portBadges[i]);
            portBadges.Clear();
            DestroyBadge(nameBadge);

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
            string key = driver.World.Database.Machines[machineId].Key;
            nameBadge = CreateBadge(title, MachineInfoPresenter.GetMachineColor(key), new Vector2(150f, 34f), 18);

            if (kind == MachineInstanceKind.Miner)
            {
                portBadges.Add(CreateBadge("AUTO → CORE", new Color(0.95f, 0.72f, 0.15f), new Vector2(120f, 30f), 14));
                return;
            }

            var processor = driver.World.Processors[instanceIndex];
            if (processor.UniversalPorts)
            {
                for (int i = 0; i < 4; i++) portBadges.Add(CreateBadge("I/O", new Color(0.35f, 0.75f, 1f), new Vector2(52f, 28f), 14));
                return;
            }

            var inputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false);
            var outputCells = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true);
            for (int i = 0; i < inputCells.Count; i++) portBadges.Add(CreateBadge("IN", new Color(0.2f, 0.72f, 1f), new Vector2(48f, 28f), 14));
            for (int i = 0; i < outputCells.Count; i++) portBadges.Add(CreateBadge("OUT", new Color(1f, 0.58f, 0.12f), new Vector2(58f, 28f), 14));
        }

        private void UpdatePositions()
        {
            if (nameBadge == null) return;
            var bounds = CalculateBounds();
            SetBadgeTransform(nameBadge, new Vector3(bounds.center.x, bounds.max.y + 0.32f, bounds.center.z), 0.0065f);

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
                SetBadgeTransform(portBadges[0], center + Vector3.left * halfX, 0.0045f);
                SetBadgeTransform(portBadges[1], center + Vector3.right * halfX, 0.0045f);
                SetBadgeTransform(portBadges[2], center + Vector3.back * halfZ, 0.0045f);
                SetBadgeTransform(portBadges[3], center + Vector3.forward * halfZ, 0.0045f);
                return;
            }

            var inputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, false);
            var outputs = GridUtility.GetPortCells(processor.Anchor, processor.Footprint, processor.Facing, true);
            int badgeIndex = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                SetBadgeTransform(portBadges[badgeIndex++], GridUtility.CellToWorldCenter(inputs[i], bounds.max.y + 0.12f), 0.0045f);
            }
            for (int i = 0; i < outputs.Count; i++)
            {
                SetBadgeTransform(portBadges[badgeIndex++], GridUtility.CellToWorldCenter(outputs[i], bounds.max.y + 0.12f), 0.0045f);
            }
        }

        private Bounds CalculateBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private void SetBadgeTransform(WorldBadge badge, Vector3 position, float scale)
        {
            if (badge == null || badge.Root == null) return;
            badge.Root.transform.position = position;
            if (targetCamera != null) badge.Root.transform.rotation = targetCamera.transform.rotation;
            badge.Root.transform.localScale = Vector3.one * scale;
        }

        private static WorldBadge CreateBadge(string label, Color color, Vector2 size, int fontSize)
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
            backgroundGO.GetComponent<Image>().color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.94f);

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

            return new WorldBadge(root);
        }

        private static void DestroyBadge(WorldBadge badge)
        {
            if (badge != null && badge.Root != null) Destroy(badge.Root);
        }

        private sealed class WorldBadge
        {
            public readonly GameObject Root;
            public WorldBadge(GameObject root) => Root = root;
        }
    }
}
