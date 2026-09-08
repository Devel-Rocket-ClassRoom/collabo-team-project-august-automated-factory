using Factory.Building;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 배치 고스트의 현재 회전 방향을 기준으로 입력·출력 위치를 실시간 표시한다.
    public sealed class GhostPortPreview : MonoBehaviour
    {
        private MachineGhostTool tool;
        private Camera targetCamera;
        private GameObject inputBadge;
        private GameObject secondInputBadge;
        private GameObject outputBadge;
        private GameObject autoBadge;
        private readonly List<GameObject> routingBadges = new List<GameObject>();

        private static readonly Vector2Int[] FourDirs =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
        };

        public void Initialize(MachineGhostTool machineGhostTool)
        {
            tool = machineGhostTool;
            if (targetCamera == null) targetCamera = Camera.main;
            EnsureBadges();
        }

        private void LateUpdate()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (!MachineGhostAdapter.TryRead(tool, out var selection))
            {
                SetBadgeVisibility(false, false);
                SetRoutingVisibility(false);
                return;
            }

            Bounds bounds = CalculateBounds(selection.Ghost);
            Vector3 center = new Vector3(bounds.center.x, bounds.max.y + 0.15f, bounds.center.z);

            if (selection.MachineId == "Miner")
            {
                SetBadgeVisibility(false, true);
                SetRoutingVisibility(false);
                SetBadgeTransform(autoBadge, center, 0.005f);
                return;
            }

            if (selection.MachineId == "Splitter" || selection.MachineId == "Merger")
            {
                SetBadgeVisibility(false, false);
                SetRoutingVisibility(true);
                bool splitter = selection.MachineId == "Splitter";
                for (int i = 0; i < FourDirs.Length; i++)
                {
                    Vector2Int dir = FourDirs[i];
                    bool input = splitter ? dir == -selection.Facing : dir != selection.Facing;
                    SetBadgeLabel(routingBadges[i], input ? "IN" : "OUT", input);
                    float offset = dir.x != 0 ? bounds.extents.x + 0.22f : bounds.extents.z + 0.22f;
                    SetBadgeTransform(routingBadges[i], center + new Vector3(dir.x, 0f, dir.y) * offset, 0.0045f);
                }
                return;
            }

            SetBadgeVisibility(true, false);
            SetRoutingVisibility(false);
            Vector3 direction = new Vector3(selection.Facing.x, 0f, selection.Facing.y).normalized;
            float sideOffset = Mathf.Abs(direction.x) > 0.5f
                ? bounds.extents.x + 0.22f
                : bounds.extents.z + 0.22f;

            if (selection.MachineId == "Synthesizer")
            {
                secondInputBadge.SetActive(true);
                Vector3 perpendicular = new Vector3(-direction.z, 0f, direction.x) * (GridHalfCell(bounds, direction));
                SetBadgeTransform(inputBadge, center - direction * sideOffset - perpendicular, 0.0045f);
                SetBadgeTransform(secondInputBadge, center - direction * sideOffset + perpendicular, 0.0045f);
            }
            else
            {
                secondInputBadge.SetActive(false);
                SetBadgeTransform(inputBadge, center - direction * sideOffset, 0.0045f);
            }
            SetBadgeTransform(outputBadge, center + direction * sideOffset, 0.0045f);
        }

        private void OnDestroy()
        {
            if (inputBadge != null) Destroy(inputBadge);
            if (secondInputBadge != null) Destroy(secondInputBadge);
            if (outputBadge != null) Destroy(outputBadge);
            if (autoBadge != null) Destroy(autoBadge);
            for (int i = 0; i < routingBadges.Count; i++) if (routingBadges[i] != null) Destroy(routingBadges[i]);
        }

        private void EnsureBadges()
        {
            if (inputBadge == null) inputBadge = CreateBadge("Ghost_IN", "IN", new Color(0.2f, 0.72f, 1f), new Vector2(54f, 30f));
            if (secondInputBadge == null) secondInputBadge = CreateBadge("Ghost_IN_2", "IN", new Color(0.2f, 0.72f, 1f), new Vector2(54f, 30f));
            if (outputBadge == null) outputBadge = CreateBadge("Ghost_OUT", "OUT", new Color(1f, 0.58f, 0.12f), new Vector2(64f, 30f));
            if (autoBadge == null) autoBadge = CreateBadge("Ghost_AUTO", "AUTO → CORE", new Color(0.95f, 0.72f, 0.15f), new Vector2(130f, 30f));
            if (routingBadges.Count == 0)
            {
                for (int i = 0; i < 4; i++) routingBadges.Add(CreateBadge("Ghost_ROUTE_" + i, "I/O", new Color(0.35f, 0.75f, 1f), new Vector2(62f, 30f)));
            }
            SetBadgeVisibility(false, false);
            SetRoutingVisibility(false);
        }

        private void SetBadgeVisibility(bool showPorts, bool showAuto)
        {
            if (inputBadge != null) inputBadge.SetActive(showPorts);
            if (secondInputBadge != null) secondInputBadge.SetActive(false);
            if (outputBadge != null) outputBadge.SetActive(showPorts);
            if (autoBadge != null) autoBadge.SetActive(showAuto);
        }

        private void SetRoutingVisibility(bool visible)
        {
            for (int i = 0; i < routingBadges.Count; i++) routingBadges[i].SetActive(visible);
        }

        private static void SetBadgeLabel(GameObject badge, string label, bool input)
        {
            var text = badge.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
            var image = badge.transform.Find("Background")?.GetComponent<Image>();
            Color color = input ? new Color(0.2f, 0.72f, 1f) : new Color(1f, 0.58f, 0.12f);
            if (image != null) image.color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.96f);
        }

        private static float GridHalfCell(Bounds bounds, Vector3 direction)
        {
            float width = Mathf.Abs(direction.x) > 0.5f ? bounds.size.z : bounds.size.x;
            return Mathf.Max(0.2f, width * 0.25f);
        }

        private void SetBadgeTransform(GameObject badge, Vector3 position, float scale)
        {
            badge.transform.position = position;
            if (targetCamera != null) badge.transform.rotation = targetCamera.transform.rotation;
            badge.transform.localScale = Vector3.one * scale;
        }

        private static Bounds CalculateBounds(GameObject ghost)
        {
            var renderers = ghost.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(ghost.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static GameObject CreateBadge(string objectName, string label, Color color, Vector2 size)
        {
            var root = new GameObject(objectName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            root.GetComponent<RectTransform>().sizeDelta = size;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.96f);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(root.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
            return root;
        }
    }
}
