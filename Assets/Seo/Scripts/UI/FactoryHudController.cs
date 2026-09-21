using Choi.SaveLoad;
using Factory.Building;
using Factory.Data;
using Factory.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Seo.UI
{
    // 팀의 배치/전력/저장 로직은 그대로 두고, 이미 존재하는 버튼을 Seo 전용 HUD로 재배치한다.
    public sealed class FactoryHudController : MonoBehaviour
    {
        private enum Category { Production, Logistics, Power, System }

        private sealed class CoreResourceEntry
        {
            public int ResourceId;
            public GameObject Root;
            public Text Amount;
        }

        private Canvas canvas;
        private RectTransform safeRoot;
        private GameObject productionPage;
        private GameObject logisticsPage;
        private GameObject powerPage;
        private GameObject systemPage;
        private GameObject dockRoot;
        private GameObject sideMenuRoot;
        private Button productionTab;
        private Button logisticsTab;
        private Button powerTab;
        private Button systemTab;
        private Button editModeButton;
        private Text categoryTitle;
        private Category? openCategory;
        private GameObject rotateButton;
        private GameObject confirmButton;
        private GameObject demolishConfirmButton;
        private GameObject cancelButton;
        private Button coreResourceButton;
        private Button powerStatusButton;
        private Text rewardedAdLabel;
        private Button rewardedAdButton;
        private GameObject coreResourcePanel;
        private GameObject powerDetailPanel;
        private GameObject exitDialogRoot;
        private Transform coreResourceContent;
        private RectTransform coreResourceViewport;
        private ScrollRect coreResourceScroll;
        private Text coreResourceEmptyText;
        private Text powerText;
        private Text toastText;
        private GameObject toastRoot;
        private BuildInputRouter buildRouter;
        private MachineGhostTool machineTool;
        private string pendingPlacementMachineId;
        private bool editModeActive;
        private float toastUntil;
        private float nextCoreResourceRefresh;
        private float nextDiscovery;
        private bool built;
        private static readonly Color ToolCardIdleColor = new Color(0.10f, 0.18f, 0.20f, 0.92f);
        private readonly List<CoreResourceEntry> coreResourceEntries = new List<CoreResourceEntry>();
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
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                HandleBackPressed();
            TrackActivePlacement();
            UpdateContextActions();
            UpdatePowerStatus();
            UpdatePowerButtonStates();
            DecorateRecipePanel();
            HideLegacyPowerPanel();
            UpdateRewardedAdLabel();

            if (coreResourcePanel != null && coreResourcePanel.activeSelf
                && Time.unscaledTime >= nextCoreResourceRefresh)
            {
                RefreshCoreResourcePanel();
                nextCoreResourceRefresh = Time.unscaledTime + 0.25f;
            }

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

        private void ShowRewardedAd()
        {
            if (Bae.SpeedBuffManager.Instance != null && Bae.SpeedBuffManager.Instance.isBuffActive) return;
            var adManager = FindFirstObjectByType<Bae.LevelPlayManager>();
            if (adManager == null || Bae.SpeedBuffManager.Instance == null)
            {
                ShowToast("광고 보상 매니저를 찾을 수 없습니다");
                return;
            }

            adManager.ShowSpeedBuffAd();
        }

        private void UpdateRewardedAdLabel()
        {
            if (rewardedAdLabel == null) return;
            var buff = Bae.SpeedBuffManager.Instance;
            bool active = buff != null && buff.isBuffActive;
            if (rewardedAdButton != null) rewardedAdButton.interactable = !active;
            rewardedAdLabel.color = active ? SeoUITheme.Current.Muted : Color.white;
            string label = active
                ? $"생산 {buff.CurrentSpeedMultiplier:0.#}배 적용 중\n남은 시간 {Mathf.CeilToInt(Mathf.Max(0f, buff.buffTimeRemaining))}초"
                : "광고 보기\n60초 동안 생산 2배";
            if (rewardedAdLabel.text != label) rewardedAdLabel.text = label;
        }

        private void BuildTopHud()
        {
            var line1 = GameObject.Find("HudLine1")?.GetComponent<Text>();
            var line2 = GameObject.Find("HudLine2")?.GetComponent<Text>();
            if (line1 != null) line1.gameObject.SetActive(false);
            if (line2 != null) line2.gameObject.SetActive(false);

            coreResourceButton = CreateTopHudButton("SeoCoreResourceButton", "▣\n자원", new Vector2(28f, -24f),
                ToggleCoreResourcePanel, SeoUITheme.Current.Primary);
            powerStatusButton = CreateTopHudButton("SeoPowerStatusButton", "⚡\n전력", new Vector2(28f, -118f),
                TogglePowerDetailPanel, SeoUITheme.Current.Warning);
            CreateTopHudButton("SeoFocusCoreButton", "◎\n코어로", new Vector2(28f, -212f),
                FocusCore, SeoUITheme.Current.Success);
            rewardedAdButton = SeoUIFactory.CreateButton(safeRoot, "SeoRewardedAdButton",
                "광고 보기\n60초 동안 생산 2배", ShowRewardedAd, ToolCardIdleColor);
            SeoUIFactory.SetRect(rewardedAdButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                Vector2.one, new Vector2(-28f, -24f), new Vector2(290f, 94f));
            rewardedAdButton.transition = Selectable.Transition.None;
            rewardedAdLabel = rewardedAdButton.GetComponentInChildren<Text>(true);
            rewardedAdLabel.fontSize = 22;
            rewardedAdLabel.color = Color.white;
            SeoUIFactory.SetRect(rewardedAdLabel.rectTransform, Vector2.zero, Vector2.one,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-28f, -16f));

            var resourceCard = SeoUIFactory.CreatePanel(safeRoot, "SeoResourceCard", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(166f, -24f), new Vector2(820f, 380f));
            resourceCard.rectTransform.pivot = new Vector2(0f, 1f);
            coreResourcePanel = resourceCard.gameObject;
            CreateCardAccent(resourceCard.transform, SeoUITheme.Current.Primary);
            var resourceTitle = SeoUIFactory.CreateText(resourceCard.transform, "Title", "코어 보유 자원", 24,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(resourceTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(28f, -12f), new Vector2(-120f, 42f));
            resourceTitle.color = SeoUITheme.Current.Primary;
            var resourceClose = SeoUIFactory.CreateButton(resourceCard.transform, "Close", "×",
                ToggleCoreResourcePanel);
            SeoUIFactory.SetRect(resourceClose.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                Vector2.one, new Vector2(-18f, -16f), new Vector2(68f, 58f));
            var resourceCloseLabel = resourceClose.GetComponentInChildren<Text>(true);
            if (resourceCloseLabel != null) resourceCloseLabel.fontSize = 38;
            var resourceViewport = new GameObject("ResourceViewport", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            resourceViewport.transform.SetParent(resourceCard.transform, false);
            coreResourceViewport = resourceViewport.GetComponent<RectTransform>();
            coreResourceViewport.anchorMin = Vector2.zero;
            coreResourceViewport.anchorMax = Vector2.one;
            coreResourceViewport.pivot = new Vector2(0.5f, 0.5f);
            coreResourceViewport.offsetMin = new Vector2(18f, 18f);
            coreResourceViewport.offsetMax = new Vector2(-42f, -66f);
            var viewportImage = resourceViewport.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

            var resourceContentObject = new GameObject("ResourceContent", typeof(RectTransform));
            resourceContentObject.transform.SetParent(resourceViewport.transform, false);
            var resourceContentRect = resourceContentObject.GetComponent<RectTransform>();
            resourceContentRect.anchorMin = new Vector2(0f, 1f);
            resourceContentRect.anchorMax = new Vector2(1f, 1f);
            resourceContentRect.pivot = new Vector2(0.5f, 1f);
            resourceContentRect.anchoredPosition = Vector2.zero;
            resourceContentRect.sizeDelta = Vector2.zero;
            coreResourceContent = resourceContentObject.transform;

            var scrollbarObject = new GameObject("ResourceScrollbar", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(resourceCard.transform, false);
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-30f, 18f);
            scrollbarRect.offsetMax = new Vector2(-18f, -66f);
            scrollbarObject.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.10f, 0.88f);

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            slidingArea.transform.SetParent(scrollbarObject.transform, false);
            var slidingRect = slidingArea.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = Vector2.zero;
            slidingRect.offsetMax = Vector2.zero;

            var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handleObject.transform.SetParent(slidingArea.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            handleObject.GetComponent<Image>().color = SeoUITheme.Current.Primary;

            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleObject.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            coreResourceScroll = resourceViewport.GetComponent<ScrollRect>();
            coreResourceScroll.viewport = coreResourceViewport;
            coreResourceScroll.content = resourceContentRect;
            coreResourceScroll.horizontal = false;
            coreResourceScroll.vertical = true;
            coreResourceScroll.movementType = ScrollRect.MovementType.Clamped;
            coreResourceScroll.inertia = true;
            coreResourceScroll.decelerationRate = 0.12f;
            coreResourceScroll.scrollSensitivity = 38f;
            coreResourceScroll.verticalScrollbar = scrollbar;
            coreResourceScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            coreResourceScroll.verticalNormalizedPosition = 1f;

            coreResourceEmptyText = SeoUIFactory.CreateText(resourceCard.transform, "Empty", "보유 자원이 없습니다", 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(coreResourceEmptyText.rectTransform, new Vector2(0f, 0f), Vector2.one,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(-40f, -70f));
            coreResourceEmptyText.color = SeoUITheme.Current.Muted;
            coreResourcePanel.SetActive(false);

            var powerCard = SeoUIFactory.CreatePanel(safeRoot, "SeoPowerCard", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(166f, -118f), new Vector2(580f, 142f));
            powerCard.rectTransform.pivot = new Vector2(0f, 1f);
            powerDetailPanel = powerCard.gameObject;
            CreateCardAccent(powerCard.transform, SeoUITheme.Current.Warning);
            var powerTitle = SeoUIFactory.CreateText(powerCard.transform, "PowerTitle", "공장 전력 현황", 23,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(powerTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(500f, 34f));
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
            powerDetailPanel.SetActive(false);

            var toastPanel = SeoUIFactory.CreatePanel(safeRoot, "SeoToast", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(460f, 52f),
                new Color(0.02f, 0.12f, 0.18f, 0.96f));
            toastPanel.rectTransform.pivot = new Vector2(0.5f, 1f);
            toastRoot = toastPanel.gameObject;
            toastText = SeoUIFactory.CreateText(toastPanel.transform, "Label", string.Empty, 20,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            toastRoot.SetActive(false);

            BuildExitDialog();
        }

        private Button CreateTopHudButton(string name, string label, Vector2 position,
            UnityEngine.Events.UnityAction action, Color accent)
        {
            var button = SeoUIFactory.CreateButton(safeRoot, name, label, action, ToolCardIdleColor);
            SeoUIFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), position, new Vector2(124f, 82f));
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.fontSize = 19;
                text.color = accent;
            }
            return button;
        }

        private void ToggleCoreResourcePanel()
        {
            if (coreResourcePanel == null) return;
            bool show = !coreResourcePanel.activeSelf;
            coreResourcePanel.SetActive(show);
            if (powerDetailPanel != null) powerDetailPanel.SetActive(false);
            if (show)
            {
                coreResourcePanel.transform.SetAsLastSibling();
                RefreshCoreResourcePanel();
            }
        }

        private void TogglePowerDetailPanel()
        {
            if (powerDetailPanel == null) return;
            bool show = !powerDetailPanel.activeSelf;
            powerDetailPanel.SetActive(show);
            if (coreResourcePanel != null) coreResourcePanel.SetActive(false);
            if (show) powerDetailPanel.transform.SetAsLastSibling();
        }

        private void RefreshCoreResourcePanel()
        {
            var driver = FindFirstObjectByType<Factory.Simulation.SimulationDriver>();
            if (driver == null || driver.World == null || coreResourceContent == null) return;

            var resources = driver.World.Database.Resources;
            if (coreResourceEntries.Count == 0)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    var resource = resources[i];
                    var card = SeoUIFactory.CreatePanel(coreResourceContent, "CoreResource_" + resource.Key,
                        new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(120f, 66f),
                        new Color(0.035f, 0.10f, 0.14f, 0.96f));
                    card.rectTransform.pivot = new Vector2(0f, 1f);

                    var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer),
                        typeof(Image));
                    iconObject.transform.SetParent(card.transform, false);
                    SeoUIFactory.SetRect(iconObject.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(3f, -7f), new Vector2(50f, 50f));
                    var icon = iconObject.GetComponent<Image>();
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    RecipeResourceIconCache.Assign(icon, resource.Key, resource.PrefabName, resource.Color);

                    var name = SeoUIFactory.CreateText(card.transform, "Name", resource.DisplayName, 14,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    SeoUIFactory.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(56f, -7f), new Vector2(58f, 24f));
                    name.resizeTextForBestFit = true;
                    name.resizeTextMinSize = 10;
                    name.resizeTextMaxSize = 14;
                    name.verticalOverflow = VerticalWrapMode.Truncate;

                    var amount = SeoUIFactory.CreateText(card.transform, "Amount", "×0", 20,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    SeoUIFactory.SetRect(amount.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, 1f), new Vector2(56f, -32f), new Vector2(58f, 28f));
                    amount.color = SeoUITheme.Current.Primary;
                    coreResourceEntries.Add(new CoreResourceEntry
                    {
                        ResourceId = i,
                        Root = card.gameObject,
                        Amount = amount,
                    });
                }
            }

            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count
                || driver.World.Processors[coreIndex] == null)
            {
                for (int i = 0; i < coreResourceEntries.Count; i++) coreResourceEntries[i].Root.SetActive(false);
                coreResourceEmptyText.text = "코어를 찾을 수 없습니다";
                coreResourceEmptyText.gameObject.SetActive(true);
                return;
            }

            var core = driver.World.Processors[coreIndex];
            int visible = 0;
            for (int i = 0; i < coreResourceEntries.Count; i++)
            {
                CoreResourceEntry entry = coreResourceEntries[i];
                int count = core.InputBuffer[entry.ResourceId];
                bool hasResource = count > 0;
                entry.Root.SetActive(hasResource);
                if (!hasResource) continue;

                int column = visible % 6;
                int row = visible / 6;
                entry.Root.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(column * 128f, -row * 74f);
                entry.Amount.text = "×" + count.ToString("N0");
                visible++;
            }

            var contentRect = coreResourceContent as RectTransform;
            if (contentRect != null)
            {
                int rowCount = Mathf.CeilToInt(visible / 6f);
                float viewportHeight = coreResourceViewport != null ? coreResourceViewport.rect.height : 0f;
                float contentHeight = rowCount > 0 ? rowCount * 74f - 8f : 0f;
                contentRect.sizeDelta = new Vector2(0f, Mathf.Max(viewportHeight, contentHeight));
            }

            coreResourceEmptyText.text = "보유 자원이 없습니다";
            coreResourceEmptyText.gameObject.SetActive(visible == 0);
        }

        private void FocusCore()
        {
            GameObject core = GameObject.Find("Core");
            var cameraRig = FindFirstObjectByType<TouchCameraRig>();
            if (core == null || cameraRig == null)
            {
                ShowToast("코어 위치를 찾을 수 없습니다");
                return;
            }

            cameraRig.FocusWorldPoint(core.transform.position);
            ShowToast("코어로 이동했습니다");
        }

        private void BuildExitDialog()
        {
            var panel = SeoUIFactory.CreatePanel(safeRoot, "SeoExitDialog", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 240f),
                new Color(0.015f, 0.04f, 0.06f, 0.99f));
            exitDialogRoot = panel.gameObject;
            var title = SeoUIFactory.CreateText(panel.transform, "Title", "게임을 종료하시겠습니까?", 28,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(-40f, 70f));

            var cancel = SeoUIFactory.CreateButton(panel.transform, "Cancel", "계속하기",
                () => exitDialogRoot.SetActive(false));
            SeoUIFactory.SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-126f, 34f),
                new Vector2(220f, 64f));
            var exit = SeoUIFactory.CreateButton(panel.transform, "Exit", "게임 종료", QuitGame,
                SeoUITheme.Current.Danger);
            SeoUIFactory.SetRect(exit.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(126f, 34f),
                new Vector2(220f, 64f));
            exitDialogRoot.SetActive(false);
        }

        private void ShowExitDialog()
        {
            if (exitDialogRoot == null) return;
            exitDialogRoot.SetActive(true);
            exitDialogRoot.transform.SetAsLastSibling();
        }

        private static void QuitGame()
        {
            Application.Quit();
        }

        private void HandleBackPressed()
        {
            if (exitDialogRoot != null && exitDialogRoot.activeSelf)
            {
                exitDialogRoot.SetActive(false);
                return;
            }

            var recipePanel = RecipeSelectionPanel.Instance;
            if (recipePanel != null && recipePanel.gameObject.activeSelf)
            {
                recipePanel.Close();
                return;
            }

            if (coreResourcePanel != null && coreResourcePanel.activeSelf)
            {
                coreResourcePanel.SetActive(false);
                return;
            }
            if (powerDetailPanel != null && powerDetailPanel.activeSelf)
            {
                powerDetailPanel.SetActive(false);
                return;
            }

            var statisticsPanel = GameObject.Find("FactoryStatisticsPanel");
            if (statisticsPanel != null && statisticsPanel.activeSelf)
            {
                statisticsPanel.SetActive(false);
                return;
            }

            var powerController = FindFirstObjectByType<PowerBuildController>();
            bool activeBuild = editModeActive
                || (buildRouter != null && buildRouter.CurrentMode != BuildInputRouter.Mode.None)
                || (powerController != null && powerController.Mode != PowerBuildMode.None);
            if (activeBuild)
            {
                CancelCurrentInteraction();
                return;
            }

            if (dockRoot != null && dockRoot.activeSelf)
            {
                CloseCategoryPanel(true);
                return;
            }

            ShowExitDialog();
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
                new Vector2(0f, 0.5f), new Vector2(164f, 0f), new Vector2(650f, 660f));
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
            systemPage = CreatePage(dock.transform, "SystemPage");

            EnsureRuntimeMachineButton("PaletteButton_ProcessingMachine", "가공기", "ProcessingMachine");

            MovePaletteButton("PaletteButton_Miner", productionPage.transform, 0, "⛏");
            MovePaletteButton("PaletteButton_Smelter", productionPage.transform, 1, string.Empty, null, "smelter");
            MovePaletteButton("PaletteButton_Former", productionPage.transform, 2, string.Empty, null, "former");
            MovePaletteButton("PaletteButton_Synthesizer", productionPage.transform, 3, string.Empty, null,
                "synthesizer");
            MovePaletteButton("PaletteButton_ProcessingMachine", productionPage.transform, 4, string.Empty, null,
                "processing");

            // 물류 설비는 방향 하나만 그려서는 역할을 구분하기 어렵다. 실제 흐름 형태를
            // 축약한 도식으로 표시한다: 직선 / 1→3 분기 / 3→1 합류 / 저장 코어.
            MovePaletteButton("PaletteButton_Belt", logisticsPage.transform, 0, string.Empty, null, "belt");
            MovePaletteButton("PaletteButton_Splitter", logisticsPage.transform, 1, string.Empty, null, "splitter");
            MovePaletteButton("PaletteButton_Merger", logisticsPage.transform, 2, string.Empty, null, "merger");
            MovePaletteButton("PaletteButton_MiniCore", logisticsPage.transform, 3, string.Empty, null, "core");
            var legacyDemolishButton = GameObject.Find("PaletteButton_Demolish");
            if (legacyDemolishButton != null) legacyDemolishButton.SetActive(false);

            BuildPowerButtons();
            BuildSystemButtons();
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
            var menu = SeoUIFactory.CreatePanel(safeRoot, "SeoToolRail", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -306f), new Vector2(132f, 734f));
            menu.rectTransform.pivot = new Vector2(0f, 1f);
            menu.color = Color.clear;
            menu.raycastTarget = false;
            sideMenuRoot = menu.gameObject;

            productionTab = CreateTab(menu.transform, "생산", "⚙", 0, Category.Production);
            logisticsTab = CreateTab(menu.transform, "물류", "⇄", 1, Category.Logistics);
            powerTab = CreateTab(menu.transform, "전력", "⚡", 2, Category.Power);
            editModeButton = CreateRailButton(menu.transform, "EditMode", "편집", "✎", 3, EnterEditMode);
            systemTab = CreateTab(menu.transform, "저장", "▣", 4, Category.System);
            sideMenuRoot.SetActive(true);
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
                new Vector2(0f, 1f), new Vector2(11f, -8f - index * 120f), new Vector2(110f, 112f));
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
            iconText.color = SeoUITheme.Current.Primary;
            SetTabState(button, false);
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

        private void EnsureRuntimeMachineButton(string objectName, string label, string machineId)
        {
            if (GameObject.Find(objectName) != null) return;
            var button = SeoUIFactory.CreateButton(safeRoot, objectName, label, () =>
            {
                if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
                if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
                if (machineTool == null || buildRouter == null) return;
                machineTool.SelectMachine(machineId);
                buildRouter.SetMode(BuildInputRouter.Mode.PlaceMachine);
                pendingPlacementMachineId = machineId;
            });
            button.gameObject.name = objectName;
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
                CreateToolDiagram(go.transform, diagramKind);
                return;
            }

            var iconText = SeoUIFactory.CreateText(go.transform, "ToolIcon", icon, 56,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            SeoUIFactory.SetRect(iconText.rectTransform, new Vector2(0.14f, 0.40f), new Vector2(0.86f, 0.80f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            iconText.color = SeoUITheme.Current.Primary;
            iconText.resizeTextForBestFit = true;
            iconText.resizeTextMinSize = 30;
            iconText.resizeTextMaxSize = 56;
            iconText.lineSpacing = 0.72f;
        }

        private static void CreateToolDiagram(Transform parent, string kind)
        {
            var root = new GameObject("ToolDiagram", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(root.GetComponent<RectTransform>(), new Vector2(0.5f, 0.64f),
                new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 62f));
            root.transform.localScale = Vector3.one * 1.08f;
            Color color = SeoUITheme.Current.Primary;

            switch (kind)
            {
                case "smelter":
                    // 용광로 몸체 + 굴뚝 + 내부 열선.
                    CreateDiagramLine(root.transform, new Vector2(-35f, 20f), new Vector2(31f, 20f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(31f, 20f), new Vector2(31f, -22f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(31f, -22f), new Vector2(-35f, -22f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-35f, -22f), new Vector2(-35f, 20f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(17f, 20f), new Vector2(17f, 31f), 7f, color);
                    CreateDiagramLine(root.transform, new Vector2(9f, 31f), new Vector2(25f, 31f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-20f, -9f), new Vector2(-20f, 10f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-3f, -9f), new Vector2(-3f, 10f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(14f, -9f), new Vector2(14f, 10f), 5f, color);
                    break;
                case "former":
                    // 위·아래 금형 사이로 내려오는 프레스 피스톤.
                    CreateDiagramLine(root.transform, new Vector2(-43f, 25f), new Vector2(43f, 25f), 7f, color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, 10f), new Vector2(12f, 25f), color);
                    CreateDiagramArrow(root.transform, new Vector2(0f, 3f), new Vector2(0f, -18f), color);
                    CreateDiagramBlock(root.transform, new Vector2(0f, -24f), new Vector2(34f, 12f), color);
                    CreateDiagramLine(root.transform, new Vector2(-43f, -31f), new Vector2(43f, -31f), 7f, color);
                    break;
                case "synthesizer":
                    // 두 재료가 중앙 조립실로 들어가 하나의 결과물로 나오는 흐름.
                    CreateDiagramBlock(root.transform, new Vector2(-42f, 20f), new Vector2(12f, 12f), color);
                    CreateDiagramBlock(root.transform, new Vector2(-42f, -20f), new Vector2(12f, 12f), color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, 20f), new Vector2(-14f, 7f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-34f, -20f), new Vector2(-14f, -7f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-14f, 16f), new Vector2(16f, 16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(16f, 16f), new Vector2(16f, -16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(16f, -16f), new Vector2(-14f, -16f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-14f, -16f), new Vector2(-14f, 16f), 5f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(12f, 12f), color);
                    CreateDiagramArrow(root.transform, new Vector2(16f, 0f), new Vector2(48f, 0f), color);
                    break;
                case "processing":
                    CreateDiagramArrow(root.transform, new Vector2(-48f, 0f), new Vector2(-22f, 0f), color);
                    CreateDiagramLine(root.transform, new Vector2(-22f, 23f), new Vector2(22f, 23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(22f, 23f), new Vector2(22f, -23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(22f, -23f), new Vector2(-22f, -23f), 5f, color);
                    CreateDiagramLine(root.transform, new Vector2(-22f, -23f), new Vector2(-22f, 23f), 5f, color);
                    CreateDiagramBlock(root.transform, Vector2.zero, new Vector2(17f, 17f), color);
                    CreateDiagramArrow(root.transform, new Vector2(22f, 0f), new Vector2(49f, 0f), color);
                    break;
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
            string[] labels = { "발전기", "전선", "송전탑" };
            string[] icons = { "⚡", "━", "♜" };
            PowerBuildMode[] modes = { PowerBuildMode.Generator, PowerBuildMode.Cable,
                PowerBuildMode.TransmissionTower };
            Color inactiveColor = ToolCardIdleColor;

            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                Color? color = inactiveColor;
                var button = SeoUIFactory.CreateButton(powerPage.transform, "PowerAction_" + labels[i], labels[i], () =>
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

        private void BuildSystemButtons()
        {
            string[] labels = { "저장", "불러오기", "게임 종료" };
            string[] icons = { "↓", "↑", "×" };
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                var button = SeoUIFactory.CreateButton(systemPage.transform, "SystemAction_" + labels[i], labels[i],
                    () =>
                    {
                        if (captured == 2)
                        {
                            ShowExitDialog();
                            return;
                        }

                        var save = FindFirstObjectByType<PowerSaveManager>();
                        if (save == null) return;
                        if (captured == 0)
                        {
                            save.Save();
                            ShowToast("공장이 저장되었습니다");
                        }
                        else ShowToast(save.Load() ? "공장을 불러왔습니다" : "저장 파일이 없습니다");
                    }, ToolCardIdleColor);
                button.transition = Selectable.Transition.None;
                LayoutToolCard(button.gameObject, i, icons[i]);
            }
        }

        private void BuildContextBar(Transform dock)
        {
            var bar = SeoUIFactory.CreatePanel(safeRoot, "SeoContextBar", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 214f), new Vector2(710f, 76f));
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);

            rotateButton = MoveActionButton("RotateButton", bar.transform, -232f);
            var rotateLabel = rotateButton != null ? rotateButton.GetComponentInChildren<Text>(true) : null;
            if (rotateLabel != null) rotateLabel.text = "회전";
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
            SetTabState(systemTab, false);
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
            if (systemPage != null) systemPage.SetActive(category == Category.System);
            if (categoryTitle != null)
                categoryTitle.text = category == Category.Production ? "생산 도구"
                    : category == Category.Logistics ? "물류 도구"
                    : category == Category.Power ? "전력 도구" : "저장·시스템";
            SetTabState(productionTab, category == Category.Production);
            SetTabState(logisticsTab, category == Category.Logistics);
            SetTabState(powerTab, category == Category.Power);
            SetTabState(systemTab, category == Category.System);
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
            SetTabState(systemTab, false);
            SetSideMenuVisible(true);
        }

        private void SetSideMenuVisible(bool visible)
        {
            // 메인 카테고리 툴바는 자원/전력/코어 버튼 아래에 항상 유지한다.
            if (sideMenuRoot != null && !sideMenuRoot.activeSelf) sideMenuRoot.SetActive(true);
        }

        private void CloseCategoryPanel(bool cancelBuildMode)
        {
            if (cancelBuildMode) CancelActiveBuildMode();
            openCategory = null;
            if (dockRoot != null) dockRoot.SetActive(false);
            SetTabState(productionTab, false);
            SetTabState(logisticsTab, false);
            SetTabState(powerTab, false);
            SetTabState(systemTab, false);
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
                image.color = selected ? new Color(0.04f, 0.42f, 0.54f, 0.98f) : ToolCardIdleColor;

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
                label.color = Color.white;

            var icon = button.transform.Find("Icon")?.GetComponent<Text>();
            if (icon != null)
                icon.color = selected ? Color.white : SeoUITheme.Current.Primary;
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
            bool placingGenerator = placingPower && powerController.Mode == PowerBuildMode.Generator;

            // 발전기도 단일 연료 입력 방향을 정해야 하므로, 고스트를 놓는 동안 같은 회전
            // 버튼을 노출한다. RotatePlacementButton이 발전기 모드에서는 전력 도구로 전달한다.
            if (rotateButton != null)
            {
                rotateButton.SetActive((placingMachine && !placingMiner) || placingGenerator);
                SetActionButtonX(rotateButton, -232f);
            }
            if (confirmButton != null)
            {
                confirmButton.SetActive(placingMachine || placingPower);
                SetActionButtonX(confirmButton, placingMiner || (placingPower && !placingGenerator) ? -116f : 0f);
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
                    : placingGenerator ? 232f : placingPower ? 116f : mode == BuildInputRouter.Mode.Demolish ? -116f : 0f;
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

            if (powerStatusButton != null)
            {
                var image = powerStatusButton.GetComponent<Image>();
                if (image != null)
                    image.color = shortage
                        ? new Color(0.42f, 0.08f, 0.07f, 0.96f)
                        : new Color(0.05f, 0.25f, 0.20f, 0.96f);
                var label = powerStatusButton.GetComponentInChildren<Text>(true);
                if (label != null) label.color = shortage
                    ? SeoUITheme.Current.Danger : SeoUITheme.Current.Success;
            }
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
            RecipeResourceIconCache.Assign(icon, resource.Key, resource.PrefabName, resource.Color);

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
        private const int TextureSize = 256;
        private static RecipeResourceIconCache instance;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> FallbackSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<Image>> Waiting = new Dictionary<string, List<Image>>();

        public static void Assign(Image target, string resourceKey, string configuredPrefabKey, Color fallbackColor)
        {
            if (target == null) return;
            target.color = Color.white;
            string prefabKey = string.IsNullOrEmpty(resourceKey)
                ? configuredPrefabKey
                : "Prefab_Item_" + resourceKey;
            if (!string.IsNullOrEmpty(prefabKey))
            {
                var fixedIcon = Resources.Load<Sprite>("ResourceIcons/" + prefabKey);
                if (fixedIcon != null)
                {
                    target.sprite = fixedIcon;
                    return;
                }
            }
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
            previewCamera.orthographicSize = Mathf.Max(0.2f, extent * 1.18f);
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
