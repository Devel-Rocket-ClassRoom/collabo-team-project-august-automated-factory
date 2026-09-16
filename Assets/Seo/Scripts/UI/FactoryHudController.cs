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
        private GameObject dockRoot;
        private GameObject sideMenuRoot;
        private Button menuToggleButton;
        private Button productionTab;
        private Button logisticsTab;
        private Button powerTab;
        private Button editModeButton;
        private Category? openCategory;
        private GameObject rotateButton;
        private GameObject confirmButton;
        private GameObject demolishConfirmButton;
        private GameObject cancelButton;
        private Text powerText;
        private Text toastText;
        private GameObject toastRoot;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        private string pendingPlacementMachineId;
        private bool editModeActive;
        private float toastUntil;
        private float nextDiscovery;
        private bool built;
        private readonly Button[] powerModeButtons = new Button[4];
        private readonly Color[] powerModeButtonColors = new Color[4];

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
            TrackActivePlacement();
            UpdateContextActions();
            UpdatePowerStatus();
            UpdatePowerButtonStates();
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
            machineTool = FindFirstObjectByType<MachineGhostTool>();
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
                new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(620f, 142f));
            var resourceRt = resourceCard.rectTransform;
            resourceRt.pivot = new Vector2(0f, 1f);
            CreateCardAccent(resourceCard.transform, SeoUITheme.Current.Primary);

            var line1 = GameObject.Find("HudLine1")?.GetComponent<Text>();
            var line2 = GameObject.Find("HudLine2")?.GetComponent<Text>();
            if (line1 != null)
            {
                line1.transform.SetParent(resourceCard.transform, false);
                SeoUIFactory.SetRect(line1.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(30f, -16f), new Vector2(560f, 36f));
                line1.fontSize = 24;
                line1.fontStyle = FontStyle.Bold;
                line1.color = SeoUITheme.Current.Primary;
            }
            if (line2 != null)
            {
                line2.transform.SetParent(resourceCard.transform, false);
                SeoUIFactory.SetRect(line2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(30f, -60f), new Vector2(560f, 64f));
                line2.fontSize = 25;
                line2.fontStyle = FontStyle.Bold;
                line2.resizeTextForBestFit = true;
                line2.resizeTextMinSize = 18;
                line2.resizeTextMaxSize = 25;
                line2.horizontalOverflow = HorizontalWrapMode.Wrap;
                line2.verticalOverflow = VerticalWrapMode.Truncate;
            }

            var powerCard = SeoUIFactory.CreatePanel(safeRoot, "SeoPowerCard", Vector2.one, Vector2.one,
                new Vector2(-28f, -24f), new Vector2(540f, 142f));
            powerCard.rectTransform.pivot = Vector2.one;
            CreateCardAccent(powerCard.transform, SeoUITheme.Current.Warning);
            var powerTitle = SeoUIFactory.CreateText(powerCard.transform, "PowerTitle", "공장 전력 현황", 23,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(powerTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(480f, 34f));
            powerTitle.color = SeoUITheme.Current.Warning;

            powerText = SeoUIFactory.CreateText(powerCard.transform, "PowerStatus", "전력 시스템 연결 중", 21,
                TextAnchor.UpperLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(powerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -54f), new Vector2(480f, 76f));
            powerText.supportRichText = true;
            powerText.resizeTextForBestFit = true;
            powerText.resizeTextMinSize = 16;
            powerText.resizeTextMaxSize = 21;
            powerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            powerText.verticalOverflow = VerticalWrapMode.Truncate;

            var toastPanel = SeoUIFactory.CreatePanel(safeRoot, "SeoToast", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(460f, 52f),
                new Color(0.02f, 0.12f, 0.18f, 0.96f));
            toastPanel.rectTransform.pivot = new Vector2(0.5f, 1f);
            toastRoot = toastPanel.gameObject;
            toastText = SeoUIFactory.CreateText(toastPanel.transform, "Label", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            toastRoot.SetActive(false);
        }

        private static void CreateCardAccent(Transform parent, Color color)
        {
            var accent = SeoUIFactory.CreatePanel(parent, "Accent", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(8f, 108f), color);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.raycastTarget = false;
        }

        private void BuildBottomDock()
        {
            var dock = SeoUIFactory.CreatePanel(safeRoot, "SeoBottomDock", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1180f, 154f));
            dock.rectTransform.pivot = new Vector2(0.5f, 0f);
            dockRoot = dock.gameObject;
            BuildSideMenu();

            productionPage = CreatePage(dock.transform, "ProductionPage");
            logisticsPage = CreatePage(dock.transform, "LogisticsPage");
            powerPage = CreatePage(dock.transform, "PowerPage");

            MovePaletteButton("PaletteButton_Miner", productionPage.transform, 0, 4);
            MovePaletteButton("PaletteButton_Smelter", productionPage.transform, 1, 4);
            MovePaletteButton("PaletteButton_Former", productionPage.transform, 2, 4);
            MovePaletteButton("PaletteButton_Synthesizer", productionPage.transform, 3, 4);

            MovePaletteButton("PaletteButton_Belt", logisticsPage.transform, 0, 3);
            MovePaletteButton("PaletteButton_Splitter", logisticsPage.transform, 1, 3);
            MovePaletteButton("PaletteButton_Merger", logisticsPage.transform, 2, 3);
            var legacyDemolishButton = GameObject.Find("PaletteButton_Demolish");
            if (legacyDemolishButton != null) legacyDemolishButton.SetActive(false);

            BuildPowerButtons();
            BuildDockCloseButton(dock.transform);
            BuildContextBar(dock.transform);
            CloseCategoryPanel(false);
        }

        private void BuildDockCloseButton(Transform dock)
        {
            var close = SeoUIFactory.CreateButton(dock, "SeoDockClose", "메뉴 닫기", () => CloseCategoryPanel(true));
            SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                new Vector2(1f, 0f), new Vector2(-8f, 14f), new Vector2(240f, 72f));
            var label = close.GetComponentInChildren<Text>(true);
            if (label != null) label.fontSize = 30;
        }

        private void BuildSideMenu()
        {
            menuToggleButton = SeoUIFactory.CreateButton(safeRoot, "SeoMenuToggle", "메뉴", ToggleSideMenu);
            SeoUIFactory.SetRect(menuToggleButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(184f, 78f));
            var toggleLabel = menuToggleButton.GetComponentInChildren<Text>(true);
            if (toggleLabel != null) toggleLabel.fontSize = 24;

            var menu = SeoUIFactory.CreatePanel(safeRoot, "SeoSideMenu", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(222f, 0f), new Vector2(264f, 416f));
            menu.rectTransform.pivot = new Vector2(0f, 0.5f);
            sideMenuRoot = menu.gameObject;

            var title = SeoUIFactory.CreateText(menu.transform, "Title", "건설 메뉴", 22,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(224f, 42f));
            title.color = SeoUITheme.Current.Primary;

            productionTab = CreateTab(menu.transform, "생산", 0, Category.Production);
            logisticsTab = CreateTab(menu.transform, "물류", 1, Category.Logistics);
            powerTab = CreateTab(menu.transform, "전력·저장", 2, Category.Power);
            editModeButton = SeoUIFactory.CreateButton(menu.transform, "EditMode", "편집 모드", EnterEditMode);
            SeoUIFactory.SetRect(editModeButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(20f, -64f - 3f * 86f), new Vector2(224f, 74f));
            var editLabel = editModeButton.GetComponentInChildren<Text>(true);
            if (editLabel != null) editLabel.fontSize = 23;
            SetTabState(editModeButton, false);
            sideMenuRoot.SetActive(false);
        }

        private void ToggleSideMenu()
        {
            if (sideMenuRoot == null) return;
            if (editModeActive)
            {
                ShowToast("취소 버튼으로 편집 모드를 종료하세요");
                return;
            }
            bool willOpen = !sideMenuRoot.activeSelf;
            if (willOpen)
            {
                CloseCategoryPanel(true);
                sideMenuRoot.SetActive(true);
            }
            else
            {
                sideMenuRoot.SetActive(false);
            }

            var label = menuToggleButton != null ? menuToggleButton.GetComponentInChildren<Text>(true) : null;
            if (label != null) label.text = willOpen ? "닫기" : "메뉴";
        }

        private Button CreateTab(Transform parent, string label, int index, Category category)
        {
            var button = SeoUIFactory.CreateButton(parent, "Tab_" + category, label, () => ToggleCategory(category));
            SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(20f, -64f - index * 86f), new Vector2(224f, 74f));
            var labelText = button.GetComponentInChildren<Text>(true);
            if (labelText != null) labelText.fontSize = 23;
            return button;
        }

        private static GameObject CreatePage(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-36f, -24f));
            return go;
        }

        private void MovePaletteButton(string objectName, Transform page, int index, int count, Color? tint = null)
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;
            go.transform.SetParent(page, false);
            var button = go.GetComponent<Button>();
            SeoUIFactory.ApplyButton(button, tint);
            if (button != null) button.onClick.AddListener(CollapseAfterToolSelection);
            float width = 250f;
            float spacing = 22f;
            float total = width * count + spacing * (count - 1);
            float x = -total * 0.5f + width * 0.5f + index * (width + spacing);
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(width, 84f));
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.fontSize = 23;
        }

        private void BuildPowerButtons()
        {
            string[] labels = { "발전기", "전선", "송전탑", "전력 철거", "SAVE", "LOAD" };
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
                        if (controller != null)
                        {
                            controller.ToggleMode(modes[captured]);
                            ShowToast(controller.Mode == modes[captured]
                                ? labels[captured] + " 모드"
                                : labels[captured] + " 모드 종료");
                            if (controller.Mode == modes[captured]) CollapseAfterToolSelection();
                        }
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
                if (i < 4)
                {
                    powerModeButtons[i] = button;
                    powerModeButtonColors[i] = color ?? Color.white;
                }

                float width = 166f;
                float spacing = 16f;
                float total = width * labels.Length + spacing * (labels.Length - 1);
                float x = -total * 0.5f + width * 0.5f + i * (width + spacing);
                SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(width, 80f));
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.fontSize = 21;
            }
        }

        private void BuildContextBar(Transform dock)
        {
            var bar = SeoUIFactory.CreatePanel(safeRoot, "SeoContextBar", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 214f), new Vector2(710f, 76f));
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);

            rotateButton = MoveActionButton("RotateButton", bar.transform, -232f);
            confirmButton = MoveActionButton("ConfirmButton", bar.transform, 0f);
            demolishConfirmButton = MoveActionButton("DemolishConfirmButton", bar.transform, 116f, SeoUITheme.Current.Danger);
            var confirmAction = confirmButton != null ? confirmButton.GetComponent<Button>() : null;
            if (confirmAction != null) confirmAction.onClick.AddListener(HandlePlacementConfirmed);
            var demolishAction = demolishConfirmButton != null ? demolishConfirmButton.GetComponent<Button>() : null;
            if (demolishAction != null) demolishAction.onClick.AddListener(HandleDemolitionConfirmed);

            var cancel = SeoUIFactory.CreateButton(bar.transform, "SeoBuildCancel", "취소", CancelCurrentInteraction);
            var confirmRt = confirmButton != null ? confirmButton.GetComponent<RectTransform>() : null;
            Vector2 actionSize = confirmRt != null ? confirmRt.sizeDelta : new Vector2(210f, 56f);
            SeoUIFactory.SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(232f, 0f), actionSize);
            var cancelLabel = cancel.GetComponentInChildren<Text>(true);
            var confirmLabel = confirmButton != null ? confirmButton.GetComponentInChildren<Text>(true) : null;
            if (cancelLabel != null && confirmLabel != null)
            {
                cancelLabel.font = confirmLabel.font;
                cancelLabel.fontSize = confirmLabel.fontSize;
                cancelLabel.fontStyle = confirmLabel.fontStyle;
                cancelLabel.color = confirmLabel.color;
            }
            cancelButton = cancel.gameObject;
            bar.gameObject.SetActive(false);
        }

        private void CancelCurrentInteraction()
        {
            editModeActive = false;
            pendingPlacementMachineId = null;
            CancelActiveBuildMode();
            var powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null && powerController.Mode != PowerBuildMode.None)
                powerController.SetMode(PowerBuildMode.None);
            ShowToast("현재 작업을 취소했습니다");
        }

        private void EnterEditMode()
        {
            CancelActiveBuildMode();
            var powerController = FindFirstObjectByType<PowerBuildController>();
            if (powerController != null && powerController.Mode != PowerBuildMode.None)
                powerController.SetMode(PowerBuildMode.None);

            editModeActive = true;
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetSideMenuVisible(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (buildRouter != null) buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            ShowToast("편집 모드 · 철거 대상을 선택하세요");
        }

        private void HandleDemolitionConfirmed()
        {
            if (!editModeActive) return;
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (buildRouter == null) return;
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.None)
                buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            ShowToast("편집 모드 유지 · 계속 철거하거나 취소로 종료");
        }

        private void TrackActivePlacement()
        {
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter == null || machineTool == null) return;
            if (buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine
                && !string.IsNullOrEmpty(machineTool.SelectedMachineId))
            {
                pendingPlacementMachineId = machineTool.SelectedMachineId;
            }
        }

        private void HandlePlacementConfirmed()
        {
            string machineId = pendingPlacementMachineId;
            if (string.IsNullOrEmpty(machineId)) return;
            if (buildRouter == null || machineTool == null) return;
            // 기존 확정 처리에서 성공했을 때만 모드가 None으로 바뀐다. 배치가 불가능한 칸에서
            // 확정을 눌렀다면 기존 고스트를 그대로 유지하고 새 고스트를 만들지 않는다.
            if (buildRouter.CurrentMode != BuildInputRouter.Mode.None) return;

            machineTool.SelectMachine(machineId);
            buildRouter.SetMode(BuildInputRouter.Mode.PlaceMachine);
            pendingPlacementMachineId = machineId;
            ShowToast("연속 설치 모드 · 취소 버튼으로 종료");
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

        private void ToggleCategory(Category category)
        {
            if (openCategory.HasValue && openCategory.Value == category)
            {
                CloseCategoryPanel(true);
                return;
            }

            SetCategory(category);
        }

        private void SetCategory(Category category)
        {
            CancelActiveBuildMode();
            openCategory = category;
            if (dockRoot != null) dockRoot.SetActive(true);
            SetSideMenuVisible(false);
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

        // 튜토리얼은 기존 버튼을 대신 만들지 않고 현재 HUD의 해당 페이지를 열어
        // 플레이어가 실제 게임 UI를 그대로 누르게 한다.
        public void OpenProductionForTutorial()
        {
            if (built) SetCategory(Category.Production);
        }

        public void OpenLogisticsForTutorial()
        {
            if (built) SetCategory(Category.Logistics);
        }

        private void CollapseAfterToolSelection()
        {
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetSideMenuVisible(false);
        }

        private void SetSideMenuVisible(bool visible)
        {
            if (sideMenuRoot != null) sideMenuRoot.SetActive(visible);
            var label = menuToggleButton != null ? menuToggleButton.GetComponentInChildren<Text>(true) : null;
            if (label != null) label.text = visible ? "닫기" : "메뉴";
        }

        private void CloseCategoryPanel(bool cancelBuildMode)
        {
            if (cancelBuildMode) CancelActiveBuildMode();
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);

            var controller = FindFirstObjectByType<PowerBuildController>();
            if (controller != null && controller.Mode != PowerBuildMode.None)
                controller.SetMode(PowerBuildMode.None);
        }

        private void CancelActiveBuildMode()
        {
            pendingPlacementMachineId = null;
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter != null && buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine)
                machineTool?.CancelPlacement();
            if (buildRouter != null && buildRouter.CurrentMode != BuildInputRouter.Mode.None)
                buildRouter.SetMode(BuildInputRouter.Mode.None);
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
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            var mode = buildRouter != null ? buildRouter.CurrentMode : BuildInputRouter.Mode.None;
            bool placingMachine = mode == BuildInputRouter.Mode.PlaceMachine;
            bool placingMiner = placingMachine && machineTool != null && machineTool.SelectedMachineId == "Miner";

            if (rotateButton != null) rotateButton.SetActive(placingMachine && !placingMiner);
            if (confirmButton != null)
            {
                confirmButton.SetActive(placingMachine);
                SetActionButtonX(confirmButton, placingMiner ? -116f : 0f);
            }
            if (demolishConfirmButton != null)
            {
                demolishConfirmButton.SetActive(mode == BuildInputRouter.Mode.Demolish);
                if (mode == BuildInputRouter.Mode.Demolish) SetActionButtonX(demolishConfirmButton, 116f);
            }
            if (cancelButton != null)
            {
                cancelButton.SetActive(mode != BuildInputRouter.Mode.None);
                float x = placingMachine ? (placingMiner ? 116f : 232f)
                    : mode == BuildInputRouter.Mode.Demolish ? -116f : 0f;
                SetActionButtonX(cancelButton, x);
            }

            var parent = rotateButton != null ? rotateButton.transform.parent.gameObject
                : demolishConfirmButton != null ? demolishConfirmButton.transform.parent.gameObject : null;
            if (parent != null) parent.SetActive(mode != BuildInputRouter.Mode.None);
        }

        private static void SetActionButtonX(GameObject button, float x)
        {
            if (button == null) return;
            var rt = button.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = new Vector2(x, 0f);
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
            const string warningColor = "#FF3028";
            string warningLight = shortage ? $"<color={warningColor}>● 전력 부족</color>" : "<color=#5CD99A>● 전력 정상</color>";
            string powerAmount = shortage
                ? $"<color={warningColor}>{grid.RequestedPower} / {grid.AvailablePower}</color>"
                : $"{grid.RequestedPower} / {grid.AvailablePower}";

            powerText.color = SeoUITheme.Current.Text;
            powerText.text =
                $"{warningLight}   사용 / 공급  {powerAmount} MW\n" +
                $"가동 기계   {grid.PoweredMachineCount} / {grid.TotalMachineCount}대";
        }

        private void UpdatePowerButtonStates()
        {
            var controller = FindFirstObjectByType<PowerBuildController>();
            PowerBuildMode activeMode = controller != null ? controller.Mode : PowerBuildMode.None;
            PowerBuildMode[] modes = { PowerBuildMode.Generator, PowerBuildMode.Cable,
                PowerBuildMode.TransmissionTower, PowerBuildMode.Remove };

            for (int i = 0; i < powerModeButtons.Length; i++)
            {
                Button button = powerModeButtons[i];
                if (button == null) continue;
                bool selected = activeMode == modes[i];
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected
                        ? Color.Lerp(SeoUITheme.Current.Primary, Color.white, 0.78f)
                        : powerModeButtonColors[i];
                }
                button.transform.localScale = selected ? Vector3.one * 1.07f : Vector3.one;
            }
        }

        private void DecorateRecipePanel()
        {
            var panel = RecipeSelectionPanel.Instance;
            if (panel == null) return;
            var root = panel.gameObject;
            if (root.transform.parent != safeRoot) root.transform.SetParent(safeRoot, false);
            if (root.activeSelf) root.transform.SetAsLastSibling();
            var rt = root.GetComponent<RectTransform>();
            SeoUIFactory.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640f, 680f));
            var rootImage = root.GetComponent<Image>();
            SeoUIFactory.ApplyPanel(rootImage);
            if (rootImage != null) rootImage.raycastTarget = true;
            var canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

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
                    button.interactable = true;
                    var buttonImage = button.GetComponent<Image>();
                    if (buttonImage != null) buttonImage.raycastTarget = true;
                    button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 76f);
                    UpdateRecipeButtonLabel(button);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(container);
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
                close.transform.SetAsLastSibling();
            }
            else
            {
                var close = root.transform.Find("SeoRecipeClose");
                if (close != null) close.SetAsLastSibling();
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
