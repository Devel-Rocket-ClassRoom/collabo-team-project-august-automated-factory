using System.Collections.Generic;
using System.Reflection;
using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Seo.UI
{
    // 공용 DemolishTool의 선택 결과를 읽어 철거 전 대상과 위험도를 보여주는 UI 어댑터.
    // 실제 삭제와 코어 보호 규칙은 기존 철거 도구가 그대로 담당한다.
    public sealed class DemolitionFeedback : MonoBehaviour
    {
        private static readonly FieldInfo SelectedField = typeof(DemolishTool).GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SelectionBoxField = typeof(DemolishTool).GetField("selectionBox", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DriverField = typeof(DemolishTool).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<(CellOccupantType type, int index)> currentSelection = new List<(CellOccupantType, int)>();
        private readonly List<(CellOccupantType type, int index)> lastSelection = new List<(CellOccupantType, int)>();
        private readonly Dictionary<Renderer, MaterialPropertyBlock> originalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();

        private BuildInputRouter router;
        private DemolishTool demolishTool;
        private SimulationDriver driver;
        private GameObject panelRoot;
        private Text summaryText;
        private Button confirmButton;
        private Button cancelButton;
        private LineRenderer selectionOutline;
        private bool wasDemolishMode;
        private float nextDiscovery;
        private float toastUntil;
        private string toastMessage;
        private int selectionSignature = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<DemolitionFeedback>() != null) return;
            new GameObject("[Seo] Demolition Feedback").AddComponent<DemolitionFeedback>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextDiscovery)
            {
                Discover();
                nextDiscovery = Time.unscaledTime + 0.4f;
            }

            if (router == null || demolishTool == null || driver == null || driver.World == null || !EnsureUI()) return;

            bool demolishMode = router.CurrentMode == BuildInputRouter.Mode.Demolish;
            if (!demolishMode)
            {
                if (wasDemolishMode) HandleDemolishModeEnded();
                wasDemolishMode = false;
                RestoreHighlights();
                SetOutlineVisible(false);
                if (cancelButton != null) cancelButton.gameObject.SetActive(false);
                if (confirmButton != null) confirmButton.interactable = false;

                bool showingToast = Time.unscaledTime < toastUntil;
                panelRoot.SetActive(showingToast);
                if (showingToast) SetSummary(toastMessage, SeoUITheme.Current.Success);
                return;
            }

            wasDemolishMode = true;
            if (cancelButton != null) cancelButton.gameObject.SetActive(true);
            panelRoot.SetActive(true);
            ReadSelection();
            UpdateOutline();
            UpdateSelectionUI();
        }

        private void Discover()
        {
            if (router == null) router = FindFirstObjectByType<BuildInputRouter>();
            if (demolishTool == null) demolishTool = FindFirstObjectByType<DemolishTool>();
            if (driver == null && demolishTool != null) driver = DriverField?.GetValue(demolishTool) as SimulationDriver;
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
        }

        private bool EnsureUI()
        {
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;
            Transform parent = canvasObject.transform.Find("SafeArea") ?? canvasObject.transform;

            if (panelRoot == null)
            {
                var panel = SeoUIFactory.CreatePanel(parent, "SeoDemolitionFeedback", new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(0f, 306f), new Vector2(820f, 88f),
                    new Color(0.13f, 0.025f, 0.025f, 0.97f));
                panel.rectTransform.pivot = new Vector2(0.5f, 0f);
                panelRoot = panel.gameObject;
                summaryText = SeoUIFactory.CreateText(panel.transform, "Summary", string.Empty, 20,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                summaryText.rectTransform.offsetMin = new Vector2(24f, 8f);
                summaryText.rectTransform.offsetMax = new Vector2(-24f, -8f);
                panelRoot.SetActive(false);
            }

            var confirmObject = GameObject.Find("DemolishConfirmButton");
            if (confirmObject != null)
            {
                confirmButton = confirmObject.GetComponent<Button>();
                StyleActionButton(confirmButton, "철거 확정");
                var confirmRect = confirmObject.GetComponent<RectTransform>();
                if (confirmRect != null)
                {
                    confirmRect.anchoredPosition = new Vector2(116f, 0f);
                    confirmRect.sizeDelta = new Vector2(210f, 56f);
                }

                Transform contextBar = confirmObject.transform.parent;
                if (cancelButton == null && contextBar != null)
                {
                    var existing = contextBar.Find("SeoDemolishCancel");
                    cancelButton = existing != null
                        ? existing.GetComponent<Button>()
                        : SeoUIFactory.CreateButton(contextBar, "SeoDemolishCancel", "취소", CancelDemolition);
                    StyleActionButton(cancelButton, "취소");
                    SeoUIFactory.SetRect(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-116f, 0f), new Vector2(210f, 56f));
                }
            }

            return panelRoot != null;
        }

        private static void StyleActionButton(Button button, string labelText)
        {
            if (button == null) return;
            SeoUIFactory.ApplyButton(button);
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            label.text = labelText;
            label.fontSize = 24;
            label.fontStyle = FontStyle.Bold;
            label.color = SeoUITheme.Current.Text;
        }

        private void ReadSelection()
        {
            currentSelection.Clear();
            var selected = SelectedField?.GetValue(demolishTool) as IEnumerable<(CellOccupantType type, int index)>;
            if (selected == null) return;
            foreach (var entry in selected) currentSelection.Add(entry);
        }

        private void UpdateSelectionUI()
        {
            bool hasTargets = currentSelection.Count > 0;
            if (confirmButton != null) confirmButton.interactable = hasTargets;

            int signature = currentSelection.Count;
            for (int i = 0; i < currentSelection.Count; i++)
                signature = unchecked(signature * 397 ^ ((int)currentSelection[i].type * 1000003 + currentSelection[i].index));

            if (signature != selectionSignature)
            {
                selectionSignature = signature;
                RestoreHighlights();
                ApplyHighlights();
                lastSelection.Clear();
                lastSelection.AddRange(currentSelection);
            }

            if (!hasTargets)
            {
                SetSummary("철거 영역을 드래그하세요 · 코어는 자동으로 제외됩니다", SeoUITheme.Current.Warning);
                return;
            }

            int machineCount = 0;
            int beltCount = 0;
            var names = new Dictionary<string, int>();
            for (int i = 0; i < currentSelection.Count; i++)
            {
                var entry = currentSelection[i];
                if (entry.type == CellOccupantType.Belt)
                {
                    beltCount++;
                    continue;
                }

                machineCount++;
                string name = ResolveMachineName(entry);
                names.TryGetValue(name, out int count);
                names[name] = count + 1;
            }

            var nameParts = new List<string>();
            foreach (var pair in names) nameParts.Add(pair.Key + " " + pair.Value);
            string detail = nameParts.Count > 0 ? string.Join(" · ", nameParts) : "기계 없음";
            SetSummary($"철거 예정 · 기계 {machineCount}개 · 벨트 {beltCount}개\n{detail} · 코어 제외",
                SeoUITheme.Current.Danger);
        }

        private string ResolveMachineName((CellOccupantType type, int index) entry)
        {
            if (entry.type == CellOccupantType.Miner)
            {
                if (entry.index < 0 || entry.index >= driver.World.Miners.Count || driver.World.Miners[entry.index] == null)
                    return "채굴기";
                return MachineInfoPresenter.GetMachineDisplayName(driver.World,
                    driver.World.Miners[entry.index].MachineId);
            }

            if (entry.index < 0 || entry.index >= driver.World.Processors.Count || driver.World.Processors[entry.index] == null)
                return "기계";
            return MachineInfoPresenter.GetMachineDisplayName(driver.World,
                driver.World.Processors[entry.index].MachineId);
        }

        private void ApplyHighlights()
        {
            for (int i = 0; i < currentSelection.Count; i++)
            {
                var entry = currentSelection[i];
                string objectName = entry.type == CellOccupantType.Belt
                    ? $"Belt_{entry.index}"
                    : entry.type == CellOccupantType.Miner
                        ? $"{MachineInstanceKind.Miner}_{entry.index}"
                        : $"{MachineInstanceKind.Processor}_{entry.index}";
                var root = GameObject.Find(objectName);
                if (root == null) continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null || originalBlocks.ContainsKey(renderer)) continue;

                    var original = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(original);
                    originalBlocks[renderer] = original;

                    var highlight = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(highlight);
                    highlight.SetColor("_BaseColor", new Color(1f, 0.08f, 0.06f, 1f));
                    highlight.SetColor("_Color", new Color(1f, 0.08f, 0.06f, 1f));
                    renderer.SetPropertyBlock(highlight);
                }
            }
        }

        private void RestoreHighlights()
        {
            foreach (var pair in originalBlocks)
            {
                if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);
            }
            originalBlocks.Clear();
            selectionSignature = int.MinValue;
        }

        private void UpdateOutline()
        {
            var box = SelectionBoxField?.GetValue(demolishTool) as GameObject;
            if (box == null || !box.activeInHierarchy)
            {
                SetOutlineVisible(false);
                return;
            }

            EnsureOutline();
            selectionOutline.gameObject.SetActive(true);
            Vector3 center = box.transform.position;
            Vector3 size = box.transform.lossyScale;
            float halfX = size.x * 0.5f;
            float halfZ = size.z * 0.5f;
            float y = center.y + 0.08f;
            selectionOutline.SetPosition(0, new Vector3(center.x - halfX, y, center.z - halfZ));
            selectionOutline.SetPosition(1, new Vector3(center.x + halfX, y, center.z - halfZ));
            selectionOutline.SetPosition(2, new Vector3(center.x + halfX, y, center.z + halfZ));
            selectionOutline.SetPosition(3, new Vector3(center.x - halfX, y, center.z + halfZ));
        }

        private void EnsureOutline()
        {
            if (selectionOutline != null) return;
            var root = new GameObject("SeoDemolitionOutline", typeof(LineRenderer));
            selectionOutline = root.GetComponent<LineRenderer>();
            selectionOutline.loop = true;
            selectionOutline.useWorldSpace = true;
            selectionOutline.positionCount = 4;
            selectionOutline.startWidth = 0.08f;
            selectionOutline.endWidth = 0.08f;
            selectionOutline.startColor = SeoUITheme.Current.Danger;
            selectionOutline.endColor = SeoUITheme.Current.Danger;
            selectionOutline.shadowCastingMode = ShadowCastingMode.Off;
            selectionOutline.receiveShadows = false;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null) selectionOutline.material = new Material(shader) { color = SeoUITheme.Current.Danger };
            root.SetActive(false);
        }

        private void SetOutlineVisible(bool visible)
        {
            if (selectionOutline != null) selectionOutline.gameObject.SetActive(visible);
        }

        private void CancelDemolition()
        {
            if (demolishTool != null) demolishTool.OnCancelled();
            if (router != null) router.SetMode(BuildInputRouter.Mode.None);
        }

        private void HandleDemolishModeEnded()
        {
            int removedMachines = 0;
            int removedBelts = 0;
            for (int i = 0; i < lastSelection.Count; i++)
            {
                var entry = lastSelection[i];
                if (!WasRemoved(entry)) continue;
                if (entry.type == CellOccupantType.Belt) removedBelts++;
                else removedMachines++;
            }

            if (removedMachines > 0 || removedBelts > 0)
            {
                toastMessage = $"철거 완료 · 기계 {removedMachines}개 · 벨트 {removedBelts}개";
                toastUntil = Time.unscaledTime + 2f;
            }

            currentSelection.Clear();
            lastSelection.Clear();
            selectionSignature = int.MinValue;
        }

        private bool WasRemoved((CellOccupantType type, int index) entry)
        {
            switch (entry.type)
            {
                case CellOccupantType.Belt:
                    return entry.index < 0 || entry.index >= driver.World.Segments.Count
                        || driver.World.Segments[entry.index] == null;
                case CellOccupantType.Miner:
                    return entry.index < 0 || entry.index >= driver.World.Miners.Count
                        || driver.World.Miners[entry.index] == null;
                case CellOccupantType.Processor:
                    return entry.index < 0 || entry.index >= driver.World.Processors.Count
                        || driver.World.Processors[entry.index] == null;
                default:
                    return false;
            }
        }

        private void SetSummary(string message, Color color)
        {
            if (summaryText == null) return;
            summaryText.text = message;
            summaryText.color = color;
        }

        private void OnDestroy()
        {
            RestoreHighlights();
            if (selectionOutline != null)
            {
                if (selectionOutline.material != null) Destroy(selectionOutline.material);
                Destroy(selectionOutline.gameObject);
            }
        }
    }
}
