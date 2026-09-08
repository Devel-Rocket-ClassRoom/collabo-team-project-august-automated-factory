using Choi.SaveLoad;
using Factory.Building;
using Factory.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 팀의 배치/전력/저장 로직은 그대로 두고, 이미 존재하는 버튼을 Seo 전용 HUD로 재배치한다.
    public sealed class FactoryHudController : MonoBehaviour
    {
        private enum Category { Production, Logistics, Power }

        private Canvas canvas;
        private RectTransform safeRoot;
        private GameObject productionPage;
        private GameObject logisticsPage;
        private GameObject powerPage;
        private Button productionTab;
        private Button logisticsTab;
        private Button powerTab;
        private GameObject rotateButton;
        private GameObject confirmButton;
        private GameObject demolishConfirmButton;
        private Text powerText;
        private Text toastText;
        private GameObject toastRoot;
        private BuildInputRouter buildRouter;
        private float toastUntil;
        private float nextDiscovery;
        private bool built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<FactoryHudController>() != null) return;
            new GameObject("[Seo] Factory HUD").AddComponent<FactoryHudController>();
        }

        private void Update()
        {
            if (!built && Time.unscaledTime >= nextDiscovery)
            {
                built = TryBuild();
                nextDiscovery = Time.unscaledTime + 0.25f;
            }

            if (!built) return;
            UpdateContextActions();
            UpdatePowerStatus();
            DecorateRecipePanel();
            HideLegacyPowerPanel();

            if (toastRoot != null) toastRoot.SetActive(Time.unscaledTime < toastUntil);
        }

        private bool TryBuild()
        {
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return false;
            canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null) return false;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = SeoUIFactory.LandscapeReference;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            safeRoot = EnsureSafeRoot(canvas.transform);
            buildRouter = FindFirstObjectByType<BuildInputRouter>();
            BuildTopHud();
            BuildBottomDock();
            DecorateRecipePanel();
            return true;
        }

        private static RectTransform EnsureSafeRoot(Transform canvasTransform)
        {
            var existing = canvasTransform.Find("SafeArea");
            var go = existing != null ? existing.gameObject : new GameObject("SafeArea", typeof(RectTransform));
            if (existing == null) go.transform.SetParent(canvasTransform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (go.GetComponent<SafeAreaFitter>() == null) go.AddComponent<SafeAreaFitter>();
            return rt;
        }

        private void BuildTopHud()
        {
            var resourceCard = SeoUIFactory.CreatePanel(safeRoot, "SeoResourceCard", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(520f, 116f));
            var resourceRt = resourceCard.rectTransform;
            resourceRt.pivot = new Vector2(0f, 1f);

            var line1 = GameObject.Find("HudLine1")?.GetComponent<Text>();
            var line2 = GameObject.Find("HudLine2")?.GetComponent<Text>();
            if (line1 != null)
            {
                line1.transform.SetParent(resourceCard.transform, false);
                SeoUIFactory.SetRect(line1.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(470f, 34f));
                line1.fontSize = 22;
                line1.fontStyle = FontStyle.Bold;
                line1.color = SeoUITheme.Current.Primary;
            }
            if (line2 != null)
            {
                line2.transform.SetParent(resourceCard.transform, false);
                SeoUIFactory.SetRect(line2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(22f, -52f), new Vector2(476f, 52f));
                line2.fontSize = 20;
                line2.resizeTextForBestFit = true;
                line2.resizeTextMinSize = 15;
                line2.resizeTextMaxSize = 20;
            }

            var powerCard = SeoUIFactory.CreatePanel(safeRoot, "SeoPowerCard", Vector2.one, Vector2.one,
                new Vector2(-28f, -24f), new Vector2(430f, 94f));
            powerCard.rectTransform.pivot = Vector2.one;
            powerText = SeoUIFactory.CreateText(powerCard.transform, "PowerStatus", "전력 시스템 연결 중", 19,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            powerText.rectTransform.offsetMin = new Vector2(22f, 12f);
            powerText.rectTransform.offsetMax = new Vector2(-22f, -12f);

            var toastPanel = SeoUIFactory.CreatePanel(safeRoot, "SeoToast", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(460f, 52f),
                new Color(0.02f, 0.12f, 0.18f, 0.96f));
            toastPanel.rectTransform.pivot = new Vector2(0.5f, 1f);
            toastRoot = toastPanel.gameObject;
            toastText = SeoUIFactory.CreateText(toastPanel.transform, "Label", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            toastRoot.SetActive(false);
        }

        private void BuildBottomDock()
        {
            var dock = SeoUIFactory.CreatePanel(safeRoot, "SeoBottomDock", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1080f, 180f));
            dock.rectTransform.pivot = new Vector2(0.5f, 0f);

            var tabs = new GameObject("Tabs", typeof(RectTransform));
            tabs.transform.SetParent(dock.transform, false);
            SeoUIFactory.SetRect(tabs.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(22f, -16f), new Vector2(520f, 48f));

            productionTab = CreateTab(tabs.transform, "생산", 0, Category.Production);
            logisticsTab = CreateTab(tabs.transform, "물류", 1, Category.Logistics);
            powerTab = CreateTab(tabs.transform, "전력·저장", 2, Category.Power);

            productionPage = CreatePage(dock.transform, "ProductionPage");
            logisticsPage = CreatePage(dock.transform, "LogisticsPage");
            powerPage = CreatePage(dock.transform, "PowerPage");

            MovePaletteButton("PaletteButton_Miner", productionPage.transform, 0, 4);
            MovePaletteButton("PaletteButton_Smelter", productionPage.transform, 1, 4);
            MovePaletteButton("PaletteButton_Former", productionPage.transform, 2, 4);
            MovePaletteButton("PaletteButton_Synthesizer", productionPage.transform, 3, 4);

            MovePaletteButton("PaletteButton_Belt", logisticsPage.transform, 0, 4);
            MovePaletteButton("PaletteButton_Splitter", logisticsPage.transform, 1, 4);
            MovePaletteButton("PaletteButton_Merger", logisticsPage.transform, 2, 4);
            MovePaletteButton("PaletteButton_Demolish", logisticsPage.transform, 3, 4, SeoUITheme.Current.Danger);

            BuildPowerButtons();
            BuildContextBar(dock.transform);
            SetCategory(Category.Production);
        }

        private Button CreateTab(Transform parent, string label, int index, Category category)
        {
            var button = SeoUIFactory.CreateButton(parent, "Tab_" + category, label, () => SetCategory(category));
            SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(index * 170f, 0f), new Vector2(158f, 44f));
            return button;
        }

        private static GameObject CreatePage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(-36f, -74f));
            return go;
        }

        private void MovePaletteButton(string objectName, Transform page, int index, int count, Color? tint = null)
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;
            go.transform.SetParent(page, false);
            var button = go.GetComponent<Button>();
            SeoUIFactory.ApplyButton(button, tint);
            float width = 210f;
            float spacing = 18f;
            float total = width * count + spacing * (count - 1);
            float x = -total * 0.5f + width * 0.5f + index * (width + spacing);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -14f), new Vector2(width, 72f));
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.fontSize = 20;
        }

        private void BuildPowerButtons()
        {
            string[] labels = { "발전기", "전선", "송신탑", "전력 철거", "SAVE", "LOAD" };
            PowerBuildMode[] modes = { PowerBuildMode.Generator, PowerBuildMode.Cable,
                PowerBuildMode.TransmissionTower, PowerBuildMode.Remove };

            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var color = i == 3 ? SeoUITheme.Current.Danger : (Color?)null;
                var button = SeoUIFactory.CreateButton(powerPage.transform, "PowerAction_" + labels[i], labels[i], () =>
                {
                    if (captured < 4)
                    {
                        var controller = FindFirstObjectByType<PowerBuildController>();
                        if (controller != null) controller.SetMode(modes[captured]);
                        ShowToast(labels[captured] + " 모드");
                    }
                    else
                    {
                        var save = FindFirstObjectByType<PowerSaveManager>();
                        if (save == null) return;
                        if (captured == 4)
                        {
                            save.Save();
                            ShowToast("공장이 저장되었습니다");
                        }
                        else ShowToast(save.Load() ? "공장을 불러왔습니다" : "저장 파일이 없습니다");
                    }
                }, color);

                float width = 148f;
                float spacing = 14f;
                float total = width * labels.Length + spacing * (labels.Length - 1);
                float x = -total * 0.5f + width * 0.5f + i * (width + spacing);
                SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, -14f), new Vector2(width, 68f));
            }
        }

        private void BuildContextBar(Transform dock)
        {
            var bar = SeoUIFactory.CreatePanel(safeRoot, "SeoContextBar", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 214f), new Vector2(470f, 76f));
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);

            rotateButton = MoveActionButton("RotateButton", bar.transform, -116f);
            confirmButton = MoveActionButton("ConfirmButton", bar.transform, 116f);
            demolishConfirmButton = MoveActionButton("DemolishConfirmButton", bar.transform, 0f, SeoUITheme.Current.Danger);
            bar.gameObject.SetActive(false);
        }

        private static GameObject MoveActionButton(string name, Transform parent, float x, Color? tint = null)
        {
            var go = GameObject.Find(name);
            if (go == null) return null;
            go.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(210f, 56f));
            SeoUIFactory.ApplyButton(go.GetComponent<Button>(), tint);
            return go;
        }

        private void SetCategory(Category category)
        {
            if (productionPage != null) productionPage.SetActive(category == Category.Production);
            if (logisticsPage != null) logisticsPage.SetActive(category == Category.Logistics);
            if (powerPage != null) powerPage.SetActive(category == Category.Power);
            SetTabState(productionTab, category == Category.Production);
            SetTabState(logisticsTab, category == Category.Logistics);
            SetTabState(powerTab, category == Category.Power);
            var controller = FindFirstObjectByType<PowerBuildController>();
            if (category != Category.Power && controller != null && controller.Mode != PowerBuildMode.None)
                controller.SetMode(PowerBuildMode.None);
        }

        private static void SetTabState(Button button, bool selected)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = selected ? Color.white : new Color(0.22f, 0.4f, 0.48f, 0.82f);

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.color = selected ? SeoUITheme.Current.Text : SeoUITheme.Current.Muted;
        }

        private void UpdateContextActions()
        {
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            var mode = buildRouter != null ? buildRouter.CurrentMode : BuildInputRouter.Mode.None;
            if (rotateButton != null) rotateButton.SetActive(mode == BuildInputRouter.Mode.PlaceMachine);
            if (confirmButton != null) confirmButton.SetActive(mode == BuildInputRouter.Mode.PlaceMachine);
            if (demolishConfirmButton != null) demolishConfirmButton.SetActive(mode == BuildInputRouter.Mode.Demolish);

            var parent = rotateButton != null ? rotateButton.transform.parent.gameObject
                : demolishConfirmButton != null ? demolishConfirmButton.transform.parent.gameObject : null;
            if (parent != null) parent.SetActive(mode == BuildInputRouter.Mode.PlaceMachine || mode == BuildInputRouter.Mode.Demolish);
        }

        private void UpdatePowerStatus()
        {
            if (powerText == null) return;
            var grid = FindFirstObjectByType<PowerGridSystem>();
            if (grid == null)
            {
                powerText.text = "전력 시스템 준비 중";
                return;
            }

            bool shortage = grid.RequestedPower > grid.AvailablePower;
            powerText.color = shortage ? SeoUITheme.Current.Warning : SeoUITheme.Current.Text;
            powerText.text = $"POWER  {grid.UsedPower} / {grid.AvailablePower}\n가동 기계  {grid.PoweredMachineCount} / {grid.TotalMachineCount}";
        }

        private void DecorateRecipePanel()
        {
            var panel = RecipeSelectionPanel.Instance;
            if (panel == null) return;
            var root = panel.gameObject;
            if (root.transform.parent != safeRoot) root.transform.SetParent(safeRoot, false);
            var rt = root.GetComponent<RectTransform>();
            SeoUIFactory.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 540f));
            SeoUIFactory.ApplyPanel(root.GetComponent<Image>());

            var container = root.transform.Find("RecipeButtonContainer") as RectTransform;
            if (container != null)
            {
                container.offsetMin = new Vector2(28f, 28f);
                container.offsetMax = new Vector2(-28f, -92f);
                for (int i = 0; i < container.childCount; i++)
                {
                    var button = container.GetChild(i).GetComponent<Button>();
                    if (button == null) continue;
                    SeoUIFactory.ApplyButton(button);
                    button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 76f);
                    UpdateRecipeButtonLabel(button);
                }
            }

            if (root.transform.Find("SeoRecipeHeader") == null)
            {
                var header = SeoUIFactory.CreateText(root.transform, "SeoRecipeHeader", "레시피 선택", 27,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                SeoUIFactory.SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(-16f, -14f), new Vector2(-120f, 62f));
                var close = SeoUIFactory.CreateButton(root.transform, "SeoRecipeClose", "닫기", panel.Close,
                    SeoUITheme.Current.Danger);
                SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                    new Vector2(-20f, -18f), new Vector2(100f, 48f));
            }
        }

        private static void HideLegacyPowerPanel()
        {
            var panel = GameObject.Find("FactoryPowerPanel");
            if (panel != null) panel.SetActive(false);
            var toggle = GameObject.Find("FactoryPowerPanelToggle");
            if (toggle != null) toggle.SetActive(false);
        }

        private static void UpdateRecipeButtonLabel(Button button)
        {
            const string prefix = "Recipe_";
            if (button == null || !button.name.StartsWith(prefix)) return;
            var driver = FindFirstObjectByType<Factory.Simulation.SimulationDriver>();
            if (driver == null || driver.World == null) return;
            string key = button.name.Substring(prefix.Length);
            if (!driver.World.Database.TryGetRecipeId(key, out int recipeId)) return;
            var recipe = driver.World.Database.Recipes[recipeId];
            var inputParts = new System.Collections.Generic.List<string>();
            var outputParts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var amount = recipe.Inputs[i];
                inputParts.Add(driver.World.Database.Resources[amount.ResourceId].DisplayName + " ×" + amount.Amount);
            }
            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                var amount = recipe.Outputs[i];
                outputParts.Add(driver.World.Database.Resources[amount.ResourceId].DisplayName + " ×" + amount.Amount);
            }
            var label = button.GetComponentInChildren<Text>(true);
            if (label == null) return;
            label.text = string.Join(" + ", inputParts) + "  →  " + string.Join(" + ", outputParts)
                + $"    {recipe.ProcessSeconds:0.#}초";
            label.fontSize = 18;
        }

        private void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastUntil = Time.unscaledTime + 2.2f;
            toastRoot.SetActive(true);
        }
    }
}
