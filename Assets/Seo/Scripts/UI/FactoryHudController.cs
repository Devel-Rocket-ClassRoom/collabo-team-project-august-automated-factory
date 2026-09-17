using Choi.SaveLoad;
using Factory.Building;
using Factory.Data;
using Factory.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
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
        private Text categoryTitle;
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
        private static readonly Color ToolCardIdleColor = new Color(0.10f, 0.18f, 0.20f, 0.92f);
        private readonly Button[] powerModeButtons = new Button[3];
        private readonly Color[] powerModeButtonColors = new Color[3];

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
            var dock = SeoUIFactory.CreatePanel(safeRoot, "SeoToolFlyout", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(348f, 0f), new Vector2(650f, 660f));
            dock.rectTransform.pivot = new Vector2(0f, 0.5f);
            dockRoot = dock.gameObject;
            BuildSideMenu();

            categoryTitle = SeoUIFactory.CreateText(dock.transform, "CategoryTitle", "생산 도구", 30,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(categoryTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(34f, -16f), new Vector2(-110f, 58f));
            categoryTitle.color = SeoUITheme.Current.Primary;

            productionPage = CreatePage(dock.transform, "ProductionPage");
            logisticsPage = CreatePage(dock.transform, "LogisticsPage");
            powerPage = CreatePage(dock.transform, "PowerPage");

            MovePaletteButton("PaletteButton_Miner", productionPage.transform, 0, "⛏");
            MovePaletteButton("PaletteButton_Smelter", productionPage.transform, 1, "♨");
            MovePaletteButton("PaletteButton_Former", productionPage.transform, 2, "▰");
            MovePaletteButton("PaletteButton_Synthesizer", productionPage.transform, 3, "⚙");

            // 물류 설비는 방향 하나만 그려서는 역할을 구분하기 어렵다. 실제 흐름 형태를
            // 축약한 도식으로 표시한다: 직선 / 1→3 분기 / 3→1 합류 / 저장 코어.
            MovePaletteButton("PaletteButton_Belt", logisticsPage.transform, 0, string.Empty, null, "belt");
            MovePaletteButton("PaletteButton_Splitter", logisticsPage.transform, 1, string.Empty, null, "splitter");
            MovePaletteButton("PaletteButton_Merger", logisticsPage.transform, 2, string.Empty, null, "merger");
            MovePaletteButton("PaletteButton_MiniCore", logisticsPage.transform, 3, string.Empty, null, "core");
            var legacyDemolishButton = GameObject.Find("PaletteButton_Demolish");
            if (legacyDemolishButton != null) legacyDemolishButton.SetActive(false);

            BuildPowerButtons();
            BuildDockCloseButton(dock.transform);
            BuildContextBar(dock.transform);
            CloseCategoryPanel(false);
        }

        private void BuildDockCloseButton(Transform dock)
        {
            var close = SeoUIFactory.CreateButton(dock, "SeoDockClose", "×", () => CloseCategoryPanel(true));
            SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                new Vector2(1f, 1f), new Vector2(-18f, -16f), new Vector2(68f, 58f));
            var label = close.GetComponentInChildren<Text>(true);
            if (label != null) label.fontSize = 38;
        }

        private void BuildSideMenu()
        {
            menuToggleButton = SeoUIFactory.CreateButton(safeRoot, "SeoMainMenuButton", "≡  메뉴",
                ToggleSideMenu);
            SeoUIFactory.SetRect(menuToggleButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(164f, 84f));
            var menuLabel = menuToggleButton.GetComponentInChildren<Text>(true);
            if (menuLabel != null) menuLabel.fontSize = 25;
            menuToggleButton.transition = Selectable.Transition.None;

            var menu = SeoUIFactory.CreatePanel(safeRoot, "SeoToolRail", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(202f, 0f), new Vector2(132f, 694f));
            menu.rectTransform.pivot = new Vector2(0f, 0.5f);
            sideMenuRoot = menu.gameObject;

            productionTab = CreateTab(menu.transform, "생산", "⚙", 0, Category.Production);
            logisticsTab = CreateTab(menu.transform, "물류", "⇄", 1, Category.Logistics);
            powerTab = CreateTab(menu.transform, "전력", "⚡", 2, Category.Power);
            editModeButton = CreateRailButton(menu.transform, "EditMode", "편집", "✎", 3, EnterEditMode);
            SetTabState(editModeButton, false);
            sideMenuRoot.SetActive(false);
            UpdateMenuToggleVisual(false);
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
                CloseCategoryPanel(true);
                sideMenuRoot.SetActive(false);
            }
            UpdateMenuToggleVisual(willOpen);
        }

        private void UpdateMenuToggleVisual(bool open)
        {
            if (menuToggleButton == null) return;
            var image = menuToggleButton.GetComponent<Image>();
            if (image != null)
                image.color = open ? Color.Lerp(SeoUITheme.Current.Primary, Color.white, 0.72f)
                    : ToolCardIdleColor;
            var label = menuToggleButton.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = open ? "×  메뉴 닫기" : "≡  메뉴";
                label.color = open ? SeoUITheme.Current.Text : SeoUITheme.Current.Muted;
            }
        }

        private Button CreateTab(Transform parent, string label, string icon, int index, Category category)
        {
            return CreateRailButton(parent, "Tab_" + category, label, icon, index, () => ToggleCategory(category));
        }

        private static Button CreateRailButton(Transform parent, string name, string label, string icon, int index,
            UnityEngine.Events.UnityAction action)
        {
            var button = SeoUIFactory.CreateButton(parent, name, label, action);
            SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(11f, -12f - index * 134f), new Vector2(110f, 122f));
            var labelText = button.GetComponentInChildren<Text>(true);
            if (labelText != null)
            {
                labelText.fontSize = 16;
                SeoUIFactory.SetRect(labelText.rectTransform, new Vector2(0.16f, 0.18f), new Vector2(0.84f, 0.42f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }
            var iconText = SeoUIFactory.CreateText(button.transform, "Icon", icon, 36,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(iconText.rectTransform, new Vector2(0.16f, 0.42f), new Vector2(0.84f, 0.84f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            iconText.color = SeoUITheme.Current.Text;
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

        private void MovePaletteButton(string objectName, Transform page, int index, string icon, Color? tint = null,
            string diagramKind = null)
        {
            var go = GameObject.Find(objectName);
            if (go == null) return;
            go.transform.SetParent(page, false);
            var button = go.GetComponent<Button>();
            SeoUIFactory.ApplyButton(button, tint);
            var image = go.GetComponent<Image>();
            if (image != null) image.color = ToolCardIdleColor;
            if (button != null) button.transition = Selectable.Transition.None;
            if (button != null) button.onClick.AddListener(CollapseAfterToolSelection);
            LayoutToolCard(go, index, icon, diagramKind);
        }

        private static void LayoutToolCard(GameObject go, int index, string icon, string diagramKind = null)
        {
            int column = index % 2;
            int row = index / 2;
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(28f + column * 282f, -88f - row * 166f), new Vector2(262f, 148f));
            var label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.fontSize = 17;
                SeoUIFactory.SetRect(label.rectTransform, new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.34f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }

            if (!string.IsNullOrEmpty(diagramKind))
            {
                CreateLogisticsDiagram(go.transform, diagramKind);
                return;
            }

            var iconText = SeoUIFactory.CreateText(go.transform, "ToolIcon", icon, 40,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(iconText.rectTransform, new Vector2(0.14f, 0.40f), new Vector2(0.86f, 0.80f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            iconText.color = SeoUITheme.Current.Primary;
            iconText.resizeTextForBestFit = true;
            iconText.resizeTextMinSize = 22;
            iconText.resizeTextMaxSize = 40;
            iconText.lineSpacing = 0.72f;
        }

        private static void CreateLogisticsDiagram(Transform parent, string kind)
        {
            var root = new GameObject("ToolDiagram", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.64f),
                new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 62f));
            Color color = SeoUITheme.Current.Primary;

            switch (kind)
            {
                case "belt":
                    CreateDiagramLine(root.transform, new Vector2(-45f, 15f), new Vector2(25f, 15f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(-45f, -15f), new Vector2(25f, -15f), 4f, color);
                    CreateDiagramBlock(root.transform, new Vector2(-30f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-8f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramBlock(root.transform, new Vector2(14f, 0f), new Vector2(10f, 22f), color);
                    CreateDiagramArrow(root.transform, new Vector2(25f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "splitter":
                    CreateDiagramLine(root.transform, new Vector2(-48f, 0f), new Vector2(-8f, 0f), 5f, color);
                    CreateDiagramArrow(root.transform, new Vector2(-8f, 0f), new Vector2(44f, 22f), color);
                    CreateDiagramArrow(root.transform, new Vector2(-8f, 0f), new Vector2(44f, -22f), color);
                    break;
                case "merger":
                    CreateDiagramLine(root.transform, new Vector2(-44f, 22f), new Vector2(8f, 0f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-44f, -22f), new Vector2(8f, 0f), 5f, color);
                    CreateDiagramArrow(root.transform, new Vector2(8f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "core":
                    CreateDiagramLine(root.transform, new Vector2(-24f, 20f), new Vector2(24f, 20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, 20f), new Vector2(24f, -20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, -20f), new Vector2(-24f, -20f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(-24f, -20f), new Vector2(-24f, 20f), 4f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(18f, 18f), color);
                    CreateDiagramLine(root.transform, new Vector2(-38f, 0f), new Vector2(-24f, 0f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(24f, 0f), new Vector2(38f, 0f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, 20f), new Vector2(0f, 29f), 4f, color);
                    CreateDiagramLine(root.transform, new Vector2(0f, -20f), new Vector2(0f, -29f), 4f, color);
                    break;
            }
        }

        private static void CreateDiagramArrow(Transform parent, Vector2 start, Vector2 end, Color color)
        {
            CreateDiagramLine(parent, start, end, 5f, color);
            Vector2 direction = (end - start).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 basePoint = end - direction * 13f;
            CreateDiagramLine(parent, end, basePoint + perpendicular * 7f, 5f, color);
            CreateDiagramLine(parent, end, basePoint - perpendicular * 7f, 5f, color);
        }

        private static void CreateDiagramLine(Transform parent, Vector2 start, Vector2 end, float thickness,
            Color color)
        {
            Vector2 delta = end - start;
            var line = CreateDiagramBlock(parent, (start + end) * 0.5f,
                new Vector2(delta.magnitude, thickness), color);
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private static Image CreateDiagramBlock(Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject("Part", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            SeoUIFactory.SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            return image;
        }

        private void BuildPowerButtons()
        {
            string[] labels = { "발전기", "전선", "송전탑", "SAVE", "LOAD" };
            string[] icons = { "⚡", "━", "♜", "↓", "↑" };
            PowerBuildMode[] modes = { PowerBuildMode.Generator, PowerBuildMode.Cable,
                PowerBuildMode.TransmissionTower };
            Color inactiveColor = ToolCardIdleColor;

            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                Color? color = inactiveColor;
                var button = SeoUIFactory.CreateButton(powerPage.transform, "PowerAction_" + labels[i], labels[i], () =>
                {
                    if (captured < 3)
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
                        if (captured == 3)
                        {
                            save.Save();
                            ShowToast("공장이 저장되었습니다");
                        }
                        else ShowToast(save.Load() ? "공장을 불러왔습니다" : "저장 파일이 없습니다");
                    }
                }, color);
                button.transition = Selectable.Transition.None;
                if (i < 3)
                {
                    powerModeButtons[i] = button;
                    powerModeButtonColors[i] = inactiveColor;
                }

                LayoutToolCard(button.gameObject, i, icons[i]);
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
            if (confirmAction != null)
            {
                confirmAction.onClick.AddListener(HandlePlacementConfirmed);
                confirmAction.onClick.AddListener(HandlePowerPlacementConfirmed);
            }
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
            SetTabState(editModeButton, false);
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
            if (powerController != null) powerController.SetMode(PowerBuildMode.Remove);

            editModeActive = true;
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetSideMenuVisible(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetTabState(editModeButton, true);
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (buildRouter != null)
            {
                buildRouter.enabled = true;
                buildRouter.SetMode(BuildInputRouter.Mode.Demolish);
            }
            ShowToast("편집 모드 · 기계·벨트·전력 시설을 선택하세요");
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

        private void HandlePowerPlacementConfirmed()
        {
            var controller = FindFirstObjectByType<PowerBuildController>();
            if (controller == null || !controller.HasPendingNodePlacement) return;
            if (controller.ConfirmPendingPlacement()) ShowToast("전력 시설을 설치했습니다");
            else ShowToast(controller.LastMessage);
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
            if (editModeActive)
            {
                ShowToast("취소 버튼으로 편집 모드를 종료하세요");
                return;
            }
            if (openCategory.HasValue && openCategory.Value == category)
            {
                CloseCategoryPanel(true);
                return;
            }

            SetCategory(category);
        }

        private void SetCategory(Category category)
        {
            editModeActive = false;
            CancelActiveBuildMode();
            openCategory = category;
            if (dockRoot != null) dockRoot.SetActive(true);
            SetSideMenuVisible(true);
            if (productionPage != null) productionPage.SetActive(category == Category.Production);
            if (logisticsPage != null) logisticsPage.SetActive(category == Category.Logistics);
            if (powerPage != null) powerPage.SetActive(category == Category.Power);
            if (categoryTitle != null)
                categoryTitle.text = category == Category.Production ? "생산 도구"
                    : category == Category.Logistics ? "물류 도구" : "전력·저장 도구";
            SetTabState(productionTab, category == Category.Production);
            SetTabState(logisticsTab, category == Category.Logistics);
            SetTabState(powerTab, category == Category.Power);
            SetTabState(editModeButton, false);
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
            SetSideMenuVisible(true);
        }

        private void SetSideMenuVisible(bool visible)
        {
            if (sideMenuRoot != null) sideMenuRoot.SetActive(visible);
            UpdateMenuToggleVisual(visible);
        }

        private void CloseCategoryPanel(bool cancelBuildMode)
        {
            if (cancelBuildMode) CancelActiveBuildMode();
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            if (!editModeActive) SetTabState(editModeButton, false);

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
            var powerController = FindFirstObjectByType<PowerBuildController>();
            bool placingPower = powerController != null && powerController.HasPendingNodePlacement;

            if (rotateButton != null) rotateButton.SetActive(placingMachine && !placingMiner);
            if (confirmButton != null)
            {
                confirmButton.SetActive(placingMachine || placingPower);
                SetActionButtonX(confirmButton, placingMiner || placingPower ? -116f : 0f);
            }
            if (demolishConfirmButton != null)
            {
                demolishConfirmButton.SetActive(mode == BuildInputRouter.Mode.Demolish);
                if (mode == BuildInputRouter.Mode.Demolish) SetActionButtonX(demolishConfirmButton, 116f);
            }
            if (cancelButton != null)
            {
                cancelButton.SetActive(mode != BuildInputRouter.Mode.None || placingPower);
                float x = placingMachine ? (placingMiner ? 116f : 232f)
                    : placingPower ? 116f : mode == BuildInputRouter.Mode.Demolish ? -116f : 0f;
                SetActionButtonX(cancelButton, x);
            }

            var parent = rotateButton != null ? rotateButton.transform.parent.gameObject
                : demolishConfirmButton != null ? demolishConfirmButton.transform.parent.gameObject : null;
            if (parent != null) parent.SetActive(mode != BuildInputRouter.Mode.None || placingPower);
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
                PowerBuildMode.TransmissionTower };

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
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 820f));
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
                container.offsetMax = new Vector2(-28f, -104f);
                var verticalLayout = container.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    verticalLayout.spacing = 10f;
                    verticalLayout.childControlHeight = false;
                    verticalLayout.childForceExpandHeight = false;
                }
                for (int i = 0; i < container.childCount; i++)
                {
                    var button = container.GetChild(i).GetComponent<Button>();
                    if (button == null) continue;
                    SeoUIFactory.ApplyButton(button);
                    button.interactable = true;
                    var buttonImage = button.GetComponent<Image>();
                    if (buttonImage != null) buttonImage.raycastTarget = true;
                    button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 104f);
                    UpdateRecipeButtonVisual(button);
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

        private static void UpdateRecipeButtonVisual(Button button)
        {
            const string prefix = "Recipe_";
            if (button == null || !button.name.StartsWith(prefix)) return;
            var driver = FindFirstObjectByType<Factory.Simulation.SimulationDriver>();
            if (driver == null || driver.World == null) return;
            string key = button.name.Substring(prefix.Length);
            if (!driver.World.Database.TryGetRecipeId(key, out int recipeId)) return;
            var recipe = driver.World.Database.Recipes[recipeId];
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            if (button.transform.Find("SeoRecipeVisual") != null) return;

            var visual = new GameObject("SeoRecipeVisual", typeof(RectTransform));
            visual.transform.SetParent(button.transform, false);
            SeoUIFactory.SetRect(visual.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20f, -10f));

            CreateRecipeCaption(visual.transform, "InputCaption", "필요", new Vector2(4f, 0f), 48f,
                SeoUITheme.Current.Warning);
            const float inputStart = 56f;
            const float inputAreaWidth = 310f;
            int inputCount = Mathf.Max(1, recipe.Inputs.Length);
            float inputTileWidth = inputAreaWidth / inputCount;
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var amount = recipe.Inputs[i];
                var resource = driver.World.Database.Resources[amount.ResourceId];
                CreateRecipeResourceTile(visual.transform, "Input_" + i, resource, amount.Amount,
                    new Vector2(inputStart + i * inputTileWidth, 0f), inputTileWidth - 4f);
            }

            var arrow = SeoUIFactory.CreateText(visual.transform, "FlowArrow", "▶", 34,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(arrow.rectTransform, new Vector2(372f, 0f), new Vector2(38f, 82f));
            arrow.color = SeoUITheme.Current.Primary;

            CreateRecipeCaption(visual.transform, "OutputCaption", "생산", new Vector2(414f, 0f), 50f,
                SeoUITheme.Current.Success);
            const float outputStart = 468f;
            const float outputAreaWidth = 220f;
            int outputCount = Mathf.Max(1, recipe.Outputs.Length);
            float outputTileWidth = outputAreaWidth / outputCount;
            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                var amount = recipe.Outputs[i];
                var resource = driver.World.Database.Resources[amount.ResourceId];
                CreateRecipeResourceTile(visual.transform, "Output_" + i, resource, amount.Amount,
                    new Vector2(outputStart + i * outputTileWidth, 0f), outputTileWidth - 4f);
            }

            var time = SeoUIFactory.CreateText(visual.transform, "ProcessTime",
                $"⏱ {recipe.ProcessSeconds:0.#}초", 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(time.rectTransform, new Vector2(694f, 0f), new Vector2(124f, 82f));
            time.color = SeoUITheme.Current.Muted;
        }

        private static void CreateRecipeCaption(Transform parent, string name, string value, Vector2 position,
            float width, Color color)
        {
            var caption = SeoUIFactory.CreateText(parent, name, value, 17, TextAnchor.MiddleCenter, FontStyle.Bold);
            SetRecipeElementRect(caption.rectTransform, position, new Vector2(width, 82f));
            caption.color = color;
        }

        private static void CreateRecipeResourceTile(Transform parent, string name, ResourceRuntime resource,
            int amount, Vector2 position, float width)
        {
            var tile = new GameObject(name, typeof(RectTransform));
            tile.transform.SetParent(parent, false);
            SetRecipeElementRect(tile.GetComponent<RectTransform>(), position, new Vector2(width, 82f));

            float iconSize = width >= 180f ? 68f : width >= 120f ? 58f : 46f;

            var iconObject = new GameObject("ResourceIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(tile.transform, false);
            var iconRect = iconObject.GetComponent<RectTransform>();
            SetRecipeElementRect(iconRect, Vector2.zero, new Vector2(iconSize, iconSize));
            var icon = iconObject.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RecipeResourceIconCache.Assign(icon, resource.PrefabName, resource.Color);

            var info = SeoUIFactory.CreateText(tile.transform, "Info",
                resource.DisplayName + "\n×" + amount, 17, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRecipeElementRect(info.rectTransform, new Vector2(iconSize + 6f, 0f),
                new Vector2(Mathf.Max(30f, width - iconSize - 8f), 76f));
            info.resizeTextForBestFit = true;
            info.resizeTextMinSize = 11;
            info.resizeTextMaxSize = 17;
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void SetRecipeElementRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            SeoUIFactory.SetRect(rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), position, size);
        }

        private void ShowToast(string message)
        {
            if (toastText == null) return;
            toastText.text = message;
            toastUntil = Time.unscaledTime + 2.2f;
            toastRoot.SetActive(true);
        }
    }

    // 게임에서 실제 사용하는 Addressables 자원 프리팹을 한 번씩 촬영해 레시피용 썸네일로 캐시한다.
    // 레시피 패널을 다시 열 때는 생성된 Sprite만 재사용하므로 프리팹 로드와 렌더 비용이 반복되지 않는다.
    internal sealed class RecipeResourceIconCache : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private const int TextureSize = 160;
        private static RecipeResourceIconCache instance;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> FallbackSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Image>> Waiting = new Dictionary<string, List<Image>>();

        public static void Assign(Image target, string prefabKey, Color fallbackColor)
        {
            if (target == null) return;
            target.color = Color.white;
            if (!string.IsNullOrEmpty(prefabKey) && Sprites.TryGetValue(prefabKey, out var cached))
            {
                target.sprite = cached;
                return;
            }

            string fallbackKey = string.IsNullOrEmpty(prefabKey)
                ? ColorUtility.ToHtmlStringRGBA(fallbackColor)
                : prefabKey;
            if (!FallbackSprites.TryGetValue(fallbackKey, out var fallback))
            {
                fallback = CreateFallbackSprite(fallbackColor);
                FallbackSprites[fallbackKey] = fallback;
            }
            target.sprite = fallback;
            if (string.IsNullOrEmpty(prefabKey)) return;

            if (Waiting.TryGetValue(prefabKey, out var targets))
            {
                targets.Add(target);
                return;
            }

            Waiting[prefabKey] = new List<Image> { target };
            EnsureInstance().StartCoroutine(LoadAndRender(prefabKey));
        }

        private static RecipeResourceIconCache EnsureInstance()
        {
            if (instance != null) return instance;
            var go = new GameObject("[Seo] Recipe Resource Icon Cache");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RecipeResourceIconCache>();
            return instance;
        }

        private static IEnumerator LoadAndRender(string prefabKey)
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(prefabKey);
            yield return handle;

            Sprite sprite = handle.Result != null ? RenderPrefab(handle.Result) : null;
            if (sprite != null) Sprites[prefabKey] = sprite;
            Addressables.Release(handle);

            if (!Waiting.TryGetValue(prefabKey, out var targets)) yield break;
            Waiting.Remove(prefabKey);
            if (sprite == null) yield break;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) targets[i].sprite = sprite;
            }
        }

        private static Sprite RenderPrefab(GameObject prefab)
        {
            var previewRoot = new GameObject("RecipeIconPreview");
            previewRoot.transform.position = new Vector3(10000f, 10000f, 10000f);
            var model = Instantiate(prefab, previewRoot.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(model, PreviewLayer);
            var behaviours = model.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) behaviours[i].enabled = false;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer) continue;
                if (!hasBounds) { bounds = renderers[i].bounds; hasBounds = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            if (!hasBounds)
            {
                Destroy(previewRoot);
                return null;
            }

            var cameraObject = new GameObject("RecipeIconCamera", typeof(Camera));
            var previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.clear;
            previewCamera.orthographic = true;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            previewCamera.orthographicSize = Mathf.Max(0.2f, extent * 1.45f);
            Vector3 viewDirection = new Vector3(1f, 0.85f, -1f).normalized;
            float distance = Mathf.Max(2f, extent * 5f);
            previewCamera.transform.position = bounds.center + viewDirection * distance;
            previewCamera.transform.LookAt(bounds.center);
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = distance * 3f;

            var lightObject = new GameObject("RecipeIconLight", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(0.9f, 0.96f, 1f);
            light.cullingMask = 1 << PreviewLayer;
            lightObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            var renderTexture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 4;
            previewCamera.targetTexture = renderTexture;
            var previous = RenderTexture.active;
            previewCamera.Render();
            RenderTexture.active = renderTexture;
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, TextureSize, TextureSize), 0, 0);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            RenderTexture.active = previous;
            previewCamera.targetTexture = null;
            renderTexture.Release();

            Destroy(renderTexture);
            Destroy(lightObject);
            Destroy(cameraObject);
            Destroy(previewRoot);
            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        private static Sprite CreateFallbackSprite(Color color)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Abs(x - center.x) + Mathf.Abs(y - center.y);
                    float alpha = Mathf.Clamp01((size * 0.44f - distance) / 2f);
                    float highlight = Mathf.Lerp(0.65f, 1.2f, (float)y / size);
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(color.r * highlight),
                        Mathf.Clamp01(color.g * highlight),
                        Mathf.Clamp01(color.b * highlight), alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
