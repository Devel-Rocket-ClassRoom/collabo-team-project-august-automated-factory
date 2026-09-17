using System.Collections.Generic;
using Bae.Data;
using Choi.SaveLoad;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 기존 HUD/씬 프리팹을 바꾸지 않고 SafeArea 아래에 붙는 생산 보고서 확장 UI.
    public sealed class FactoryStatisticsPanel : MonoBehaviour
    {
        private enum SortMode
        {
            ProductionHigh,
            ProductionLow,
            MachineHigh,
            MachineLow,
        }

        private sealed class ProductEntry
        {
            public int ResourceId;
            public string MachineKey;
            public string MachineLabel;
            public int MachineOrder;
        }

        private sealed class ResourceRow
        {
            public int ResourceId;
            public string MachineLabel;
            public int MachineOrder;
            public RectTransform Rect;
            public float TargetY;
            public Text Rates;
            public Text State;
            public Image ProductionFill;
            public Image ConsumptionFill;
        }

        private sealed class SectionHeader
        {
            public int MachineOrder;
            public RectTransform Rect;
            public float TargetY;
        }

        private const float RefreshInterval = 0.25f;
        private readonly List<ResourceRow> rows = new List<ResourceRow>();
        private readonly List<SectionHeader> sectionHeaders = new List<SectionHeader>();
        private SimulationDriver driver;
        private PowerGridSystem powerGrid;
        private GameObject panelRoot;
        private Transform rowContainer;
        private Text periodText;
        private Text actualPowerText;
        private Text optimalPowerText;
        private Text supplyPowerText;
        private Text powerStateText;
        private Image actualPowerFill;
        private Image optimalPowerFill;
        private Image supplyPowerFill;
        private Button sortButton;
        private float nextDiscovery;
        private float nextRefresh;
        private bool rowsBuilt;
        private SortMode sortMode = SortMode.ProductionHigh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<FactoryStatisticsPanel>() != null) return;
            new GameObject("[Seo] Factory Statistics").AddComponent<FactoryStatisticsPanel>();
        }

        private void Update()
        {
            if (driver == null || powerGrid == null || panelRoot == null)
            {
                if (Time.unscaledTime < nextDiscovery) return;
                DiscoverAndBuild();
                nextDiscovery = Time.unscaledTime + 0.25f;
            }

            if (driver != null && driver.World != null && powerGrid != null)
                driver.World.Statistics.RecordPower(CalculateActivePower(), Time.deltaTime);

            if (panelRoot != null && panelRoot.activeSelf) AnimateRows();

            if (panelRoot == null || !panelRoot.activeSelf || Time.unscaledTime < nextRefresh) return;
            Refresh();
            nextRefresh = Time.unscaledTime + RefreshInterval;
        }

        private void DiscoverAndBuild()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (powerGrid == null) powerGrid = FindFirstObjectByType<PowerGridSystem>();
            if (panelRoot != null) return;
            var canvasObject = GameObject.Find("HUDCanvas");
            if (canvasObject == null) return;
            Transform parent = canvasObject.transform.Find("SafeArea") ?? canvasObject.transform;
            Build(parent);
        }

        private void Build(Transform parent)
        {
            var openButton = SeoUIFactory.CreateButton(parent, "SeoStatisticsButton", "생산 보고서",
                TogglePanel, new Color(0.08f, 0.38f, 0.5f, 1f));
            // FactoryHudController의 왼쪽 '메뉴' 버튼 바로 아래에 같은 폭으로 배치한다.
            SeoUIFactory.SetRect(openButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, -92f), new Vector2(184f, 66f));
            openButton.GetComponentInChildren<Text>(true).fontSize = 22;

            var panel = SeoUIFactory.CreatePanel(parent, "FactoryStatisticsPanel", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 820f));
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panelRoot = panel.gameObject;
            var title = CreateText(panel.transform, "Title", "생산 흐름 보고서", 30, FontStyle.Bold,
                new Vector2(32f, -18f), new Vector2(700f, 46f));
            title.color = SeoUITheme.Current.Primary;
            periodText = CreateText(panel.transform, "Period", "측정 준비 중", 17, FontStyle.Normal,
                new Vector2(34f, -62f), new Vector2(720f, 28f));
            periodText.color = SeoUITheme.Current.Muted;
            var close = SeoUIFactory.CreateButton(panel.transform, "Close", "닫기", TogglePanel,
                SeoUITheme.Current.Danger);
            SeoUIFactory.SetRect(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24f, -20f), new Vector2(116f, 50f));

            BuildPowerReport(panel.transform);
            BuildResourceHeader(panel.transform);
            BuildScrollableList(panel.transform);
            panelRoot.SetActive(false);
        }

        private void BuildScrollableList(Transform parent)
        {
            var scrollObject = new GameObject("ResourceScroll", typeof(RectTransform), typeof(Image),
                typeof(Mask), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            SeoUIFactory.SetRect(scrollObject.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -350f),
                new Vector2(1184f, 438f));
            var viewportImage = scrollObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.08f);
            scrollObject.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("ResourceRows", typeof(RectTransform));
            content.transform.SetParent(scrollObject.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            rowContainer = content.transform;

            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = scrollObject.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.12f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 42f;
        }

        private void BuildPowerReport(Transform parent)
        {
            var card = SeoUIFactory.CreatePanel(parent, "PowerReport", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -104f), new Vector2(1184f, 184f), SeoUITheme.Current.Card);
            card.rectTransform.pivot = new Vector2(0f, 1f);
            var accent = SeoUIFactory.CreatePanel(card.transform, "Accent", new Vector2(0f, 0f),
                new Vector2(0f, 1f), Vector2.zero, new Vector2(7f, 0f), SeoUITheme.Current.Warning);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.raycastTarget = false;
            var heading = CreateText(card.transform, "Heading", "전력 가동 상태", 22, FontStyle.Bold,
                new Vector2(24f, -12f), new Vector2(320f, 32f));
            heading.color = SeoUITheme.Current.Warning;
            powerStateText = CreateText(card.transform, "State", "분석 중", 18, FontStyle.Bold,
                new Vector2(830f, -14f), new Vector2(320f, 30f));
            powerStateText.alignment = TextAnchor.UpperRight;
            actualPowerText = CreatePowerColumn(card.transform, "Actual", "실제 가동", 24f,
                SeoUITheme.Current.Success, out actualPowerFill);
            optimalPowerText = CreatePowerColumn(card.transform, "Optimal", "최적 가동 요구", 406f,
                SeoUITheme.Current.Warning, out optimalPowerFill);
            supplyPowerText = CreatePowerColumn(card.transform, "Supply", "현재 공급", 788f,
                SeoUITheme.Current.Primary, out supplyPowerFill);
        }

        private static Text CreatePowerColumn(Transform parent, string name, string label, float x,
            Color color, out Image fill)
        {
            var labelText = CreateText(parent, name + "Label", label, 17, FontStyle.Bold,
                new Vector2(x, -54f), new Vector2(330f, 26f));
            labelText.color = color;
            var value = CreateText(parent, name + "Value", "0 MW", 25, FontStyle.Bold,
                new Vector2(x, -80f), new Vector2(330f, 38f));
            var background = SeoUIFactory.CreatePanel(parent, name + "Bar", new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(x, -132f), new Vector2(330f, 14f),
                new Color(1f, 1f, 1f, 0.1f));
            background.rectTransform.pivot = new Vector2(0f, 1f);
            fill = SeoUIFactory.CreatePanel(background.transform, "Fill", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, color);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            return value;
        }

        private void BuildResourceHeader(Transform parent)
        {
            var title = CreateText(parent, "OreTitle", "자원별 생산·소비", 22, FontStyle.Bold,
                new Vector2(32f, -304f), new Vector2(300f, 34f));
            title.color = SeoUITheme.Current.Primary;
            var columns = CreateText(parent, "Columns",
                "실제 생산 / 최적 생산                 실제 소비 / 최적 소비", 16, FontStyle.Bold,
                new Vector2(430f, -306f), new Vector2(510f, 30f));
            columns.color = SeoUITheme.Current.Muted;
            columns.alignment = TextAnchor.UpperRight;
            sortButton = SeoUIFactory.CreateButton(parent, "StatisticsSort", SortLabel(), CycleSort,
                new Color(0.08f, 0.32f, 0.43f, 1f));
            SeoUIFactory.SetRect(sortButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one,
                Vector2.one, new Vector2(-30f, -300f), new Vector2(238f, 40f));
            sortButton.GetComponentInChildren<Text>(true).fontSize = 16;
            var hint = CreateText(parent, "ScrollHint", "휠 또는 드래그로 전체 생산물 보기", 14,
                FontStyle.Normal, new Vector2(32f, -330f), new Vector2(360f, 22f));
            hint.color = SeoUITheme.Current.Muted;
        }

        private void BuildRows()
        {
            if (rowsBuilt || driver == null || driver.World == null || rowContainer == null) return;
            rowsBuilt = true;
            List<ProductEntry> entries = BuildProductEntries();
            bool descending = sortMode == SortMode.ProductionHigh || sortMode == SortMode.MachineHigh;
            entries.Sort((a, b) => CompareProduction(a, b, descending));

            float y = 0f;
            bool grouped = sortMode == SortMode.MachineHigh || sortMode == SortMode.MachineLow;
            if (!grouped)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    ProductEntry entry = entries[i];
                    CreateResourceRow(entry.ResourceId, y, entry.MachineKey, entry.MachineLabel,
                        entry.MachineOrder, rows.Count);
                    y += 70f;
                }
            }
            else
            {
                string[] labels = { "채굴기", "제련로", "성형기", "가공기", "합성기" };
                for (int machineOrder = 0; machineOrder < labels.Length; machineOrder++)
                {
                    var resources = new List<int>();
                    string machineKey = null;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].MachineOrder != machineOrder) continue;
                        resources.Add(entries[i].ResourceId);
                        machineKey = entries[i].MachineKey;
                    }
                    if (resources.Count > 0)
                        AddMachineSection(labels[machineOrder], machineKey, machineOrder, resources, ref y);
                }
            }

            var contentRect = (RectTransform)rowContainer;
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(438f, y));
        }

        private List<ProductEntry> BuildProductEntries()
        {
            var result = new List<ProductEntry>();
            var assigned = new HashSet<int>();
            var deposits = driver.World.Database.OreDeposits;
            for (int i = 0; i < deposits.Count; i++)
                if (assigned.Add(deposits[i].ResourceId))
                    result.Add(new ProductEntry { ResourceId = deposits[i].ResourceId, MachineKey = "Miner",
                        MachineLabel = "채굴기", MachineOrder = 0 });
            AddRecipeEntries(result, "Smelter", "제련로", 1, assigned);
            AddRecipeEntries(result, "Former", "성형기", 2, assigned);
            AddRecipeEntries(result, "ProcessingMachine", "가공기", 3, assigned);
            AddRecipeEntries(result, "Synthesizer", "합성기", 4, assigned);
            return result;
        }

        private void AddRecipeEntries(List<ProductEntry> result, string machineKey, string machineLabel,
            int machineOrder, HashSet<int> assigned)
        {
            var recipes = driver.World.Database.Recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].RequiredMachineId != machineKey) continue;
                for (int n = 0; n < recipes[i].Outputs.Length; n++)
                {
                    int resourceId = recipes[i].Outputs[n].ResourceId;
                    if (assigned.Add(resourceId)) result.Add(new ProductEntry
                    {
                        ResourceId = resourceId,
                        MachineKey = machineKey,
                        MachineLabel = machineLabel,
                        MachineOrder = machineOrder,
                    });
                }
            }
        }

        private int CompareProduction(ProductEntry a, ProductEntry b, bool descending)
        {
            FactoryStatistics statistics = driver.World.Statistics;
            float aRate = statistics.GetProducedPerHour(a.ResourceId);
            float bRate = statistics.GetProducedPerHour(b.ResourceId);
            int result = aRate.CompareTo(bRate);
            if (descending) result = -result;
            if (result != 0) return result;
            return string.Compare(driver.World.Database.Resources[a.ResourceId].DisplayName,
                driver.World.Database.Resources[b.ResourceId].DisplayName,
                System.StringComparison.CurrentCulture);
        }

        private void CycleSort()
        {
            sortMode = (SortMode)(((int)sortMode + 1) % 4);
            if (sortButton != null) sortButton.GetComponentInChildren<Text>(true).text = SortLabel();
            RebuildRows();
            Refresh();
        }

        private string SortLabel()
        {
            switch (sortMode)
            {
                case SortMode.ProductionHigh: return "생산량 높은순";
                case SortMode.ProductionLow: return "생산량 낮은순";
                case SortMode.MachineLow: return "기계별 · 낮은순";
                default: return "기계별 · 높은순";
            }
        }

        private void RebuildRows()
        {
            rows.Clear();
            sectionHeaders.Clear();
            for (int i = rowContainer.childCount - 1; i >= 0; i--)
            {
                rowContainer.GetChild(i).gameObject.SetActive(false);
                Destroy(rowContainer.GetChild(i).gameObject);
            }
            rowsBuilt = false;
            BuildRows();
            var scroll = rowContainer.parent.GetComponent<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private void UpdateSortTargets()
        {
            if (rows.Count == 0 || driver == null || driver.World == null) return;
            bool descending = sortMode == SortMode.ProductionHigh || sortMode == SortMode.MachineHigh;
            var ordered = new List<ResourceRow>(rows);
            ordered.Sort((a, b) => CompareRows(a, b, descending));

            float y = 0f;
            bool grouped = sortMode == SortMode.MachineHigh || sortMode == SortMode.MachineLow;
            if (!grouped)
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].TargetY = y;
                    y += 70f;
                }
            }
            else
            {
                for (int machineOrder = 0; machineOrder < 5; machineOrder++)
                {
                    SectionHeader header = sectionHeaders.Find(item => item.MachineOrder == machineOrder);
                    if (header == null) continue;
                    header.TargetY = y;
                    y += 46f;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        if (ordered[i].MachineOrder != machineOrder) continue;
                        ordered[i].TargetY = y;
                        y += 70f;
                    }
                    y += 8f;
                }
            }
            ((RectTransform)rowContainer).sizeDelta = new Vector2(0f, Mathf.Max(438f, y));
        }

        private int CompareRows(ResourceRow a, ResourceRow b, bool descending)
        {
            float aRate = driver.World.Statistics.GetProducedPerHour(a.ResourceId);
            float bRate = driver.World.Statistics.GetProducedPerHour(b.ResourceId);
            int result = aRate.CompareTo(bRate);
            if (descending) result = -result;
            if (result != 0) return result;
            return string.Compare(driver.World.Database.Resources[a.ResourceId].DisplayName,
                driver.World.Database.Resources[b.ResourceId].DisplayName,
                System.StringComparison.CurrentCulture);
        }

        private void AnimateRows()
        {
            float blend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            for (int i = 0; i < rows.Count; i++)
            {
                RectTransform rect = rows[i].Rect;
                if (rect == null) continue;
                rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition,
                    new Vector2(0f, -rows[i].TargetY), blend);
            }
            for (int i = 0; i < sectionHeaders.Count; i++)
            {
                RectTransform rect = sectionHeaders[i].Rect;
                if (rect == null) continue;
                rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition,
                    new Vector2(0f, -sectionHeaders[i].TargetY), blend);
            }
        }

        private void AddMachineSection(string label, string machineKey, int machineOrder,
            List<int> resourceIds, ref float y)
        {
            if (resourceIds.Count == 0) return;
            CreateMachineHeader(label, machineOrder, resourceIds.Count, y);
            y += 46f;
            for (int i = 0; i < resourceIds.Count; i++)
            {
                CreateResourceRow(resourceIds[i], y, machineKey, label, machineOrder, rows.Count);
                y += 70f;
            }
            y += 8f;
        }

        private void CreateMachineHeader(string label, int machineOrder, int count, float y)
        {
            var header = SeoUIFactory.CreatePanel(rowContainer, "Section_" + label,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y),
                new Vector2(1184f, 38f), new Color(0.02f, 0.2f, 0.27f, 0.98f));
            header.rectTransform.pivot = new Vector2(0f, 1f);
            var text = CreateText(header.transform, "Label", $"{label}  ──────────  생산 품목 {count}종",
                18, FontStyle.Bold, new Vector2(18f, -6f), new Vector2(600f, 28f));
            text.color = SeoUITheme.Current.Primary;
            sectionHeaders.Add(new SectionHeader
            {
                MachineOrder = machineOrder,
                Rect = header.rectTransform,
                TargetY = y,
            });
        }

        private void CreateResourceRow(int resourceId, float y, string machineKey, string machineLabel,
            int machineOrder, int index)
        {
            var resource = driver.World.Database.Resources[resourceId];
            var card = SeoUIFactory.CreatePanel(rowContainer, machineKey + "_" + resource.Key,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y),
                new Vector2(1184f, 62f), index % 2 == 0
                    ? new Color(0.035f, 0.1f, 0.15f, 0.96f)
                    : new Color(0.025f, 0.075f, 0.12f, 0.96f));
            card.rectTransform.pivot = new Vector2(0f, 1f);
            var chip = SeoUIFactory.CreatePanel(card.transform, "Color", new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(18f, 38f), resource.Color);
            chip.rectTransform.pivot = new Vector2(0f, 0.5f);
            chip.raycastTarget = false;
            CreateText(card.transform, "Name", resource.DisplayName, 20, FontStyle.Bold,
                new Vector2(50f, -8f), new Vector2(210f, 30f));
            var state = CreateText(card.transform, "State", "대기", 15, FontStyle.Bold,
                new Vector2(50f, -34f), new Vector2(210f, 22f));
            state.color = SeoUITheme.Current.Muted;
            var rates = CreateText(card.transform, "Rates", string.Empty, 18, FontStyle.Bold,
                new Vector2(760f, -18f), new Vector2(390f, 30f));
            rates.alignment = TextAnchor.UpperRight;
            rows.Add(new ResourceRow
            {
                ResourceId = resourceId,
                MachineLabel = machineLabel,
                MachineOrder = machineOrder,
                Rect = card.rectTransform,
                TargetY = y,
                Rates = rates,
                State = state,
                ProductionFill = CreateFlowBar(card.transform, "Production", new Vector2(278f, -13f),
                    SeoUITheme.Current.Primary),
                ConsumptionFill = CreateFlowBar(card.transform, "Consumption", new Vector2(278f, -38f),
                    SeoUITheme.Current.Warning),
            });
        }

        private static Image CreateFlowBar(Transform parent, string name, Vector2 position, Color color)
        {
            var background = SeoUIFactory.CreatePanel(parent, name + "Bar", new Vector2(0f, 1f),
                new Vector2(0f, 1f), position, new Vector2(450f, 12f), new Color(1f, 1f, 1f, 0.09f));
            background.rectTransform.pivot = new Vector2(0f, 1f);
            var fill = SeoUIFactory.CreatePanel(background.transform, "Fill", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, color);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            return fill;
        }

        private void TogglePanel()
        {
            if (panelRoot == null) return;
            panelRoot.SetActive(!panelRoot.activeSelf);
            if (!panelRoot.activeSelf) return;
            panelRoot.transform.SetAsLastSibling();
            BuildRows();
            Refresh();
        }

        private void Refresh()
        {
            if (driver == null || driver.World == null)
            {
                periodText.text = "시뮬레이션 연결 중";
                return;
            }
            BuildRows();
            UpdateSortTargets();
            FactoryStatistics statistics = driver.World.Statistics;
            float seconds = statistics.ObservedSeconds;
            periodText.text = seconds < 1f ? "측정 준비 중"
                : $"최근 {seconds:0}초 실제 가동 기록 · 설치 설비의 이론 최대치와 비교";

            int actualPower = CalculateActivePower();
            int optimalPower = powerGrid != null ? powerGrid.RequestedPower : CalculateConfiguredPower();
            int supplyPower = powerGrid != null ? powerGrid.AvailablePower : 0;
            actualPowerText.text = $"{actualPower:N0} MW";
            optimalPowerText.text = $"{optimalPower:N0} MW";
            supplyPowerText.text = $"{supplyPower:N0} MW";
            float scale = Mathf.Max(1f, actualPower, optimalPower, supplyPower);
            actualPowerFill.fillAmount = actualPower / scale;
            optimalPowerFill.fillAmount = optimalPower / scale;
            supplyPowerFill.fillAmount = supplyPower / scale;
            bool insufficient = optimalPower > supplyPower;
            bool idle = !insufficient && optimalPower > 0 && actualPower < optimalPower;
            powerStateText.text = insufficient ? "● 공급 부족" : idle ? "● 일부 설비 대기 중" : "● 정상 가동";
            powerStateText.color = insufficient ? SeoUITheme.Current.Danger
                : idle ? SeoUITheme.Current.Warning : SeoUITheme.Current.Success;

            var theoreticalProduction = new float[driver.World.Database.ResourceCount];
            var theoreticalConsumption = new float[driver.World.Database.ResourceCount];
            CalculateTheoreticalRates(theoreticalProduction, theoreticalConsumption);
            for (int i = 0; i < rows.Count; i++)
            {
                ResourceRow row = rows[i];
                float actualProduced = statistics.GetProducedPerHour(row.ResourceId);
                float actualConsumed = statistics.GetConsumedPerHour(row.ResourceId);
                float optimalProduced = theoreticalProduction[row.ResourceId];
                float optimalConsumed = theoreticalConsumption[row.ResourceId];
                row.ProductionFill.fillAmount = Ratio(actualProduced, optimalProduced);
                row.ConsumptionFill.fillAmount = Ratio(actualConsumed, optimalConsumed);
                row.Rates.text = $"{actualProduced:N0} / {optimalProduced:N0}       " +
                    $"{actualConsumed:N0} / {optimalConsumed:N0}";
                bool hasLine = optimalProduced > 0.01f || optimalConsumed > 0.01f;
                bool bottleneck = seconds >= 5f && optimalProduced > 0.01f
                    && actualProduced < optimalProduced * 0.9f;
                row.State.text = !hasLine ? row.MachineLabel + " · 설비 없음"
                    : bottleneck ? row.MachineLabel + " · 병목 감지" : row.MachineLabel + " · 흐름 정상";
                row.State.color = !hasLine ? SeoUITheme.Current.Muted
                    : bottleneck ? SeoUITheme.Current.Danger : SeoUITheme.Current.Success;
            }
        }

        private void CalculateTheoreticalRates(float[] produced, float[] consumed)
        {
            var world = driver.World;
            for (int i = 0; i < world.Miners.Count; i++)
            {
                var miner = world.Miners[i];
                if (miner != null && miner.MineIntervalSeconds > 0f)
                    produced[miner.OutputResourceId] += miner.YieldPerCycle * 3600f / miner.MineIntervalSeconds;
            }
            for (int i = 0; i < world.Processors.Count; i++)
            {
                var processor = world.Processors[i];
                if (processor == null || processor.UniversalPorts) continue;
                int recipeId = powerGrid != null
                    ? powerGrid.GetConfiguredRecipeId(processor) : processor.RecipeId;
                if (recipeId < 0) continue;
                var recipe = world.Database.Recipes[recipeId];
                if (recipe.ProcessSeconds <= 0f) continue;
                float cycles = 3600f / recipe.ProcessSeconds;
                for (int n = 0; n < recipe.Inputs.Length; n++)
                    consumed[recipe.Inputs[n].ResourceId] += recipe.Inputs[n].Amount * cycles;
                for (int n = 0; n < recipe.Outputs.Length; n++)
                    produced[recipe.Outputs[n].ResourceId] += recipe.Outputs[n].Amount * cycles;
            }
        }

        private int CalculateActivePower()
        {
            if (driver == null || driver.World == null) return 0;
            int total = 0;
            var world = driver.World;
            for (int i = 0; i < world.Miners.Count; i++)
            {
                var miner = world.Miners[i];
                if (miner != null && miner.IsPowered
                    && miner.BufferedOutput < SimulationConstants.ResourceBufferCapacity)
                    total += MachinePower(world.Database.Machines[miner.MachineId].Key);
            }
            for (int i = 0; i < world.Processors.Count; i++)
            {
                var processor = world.Processors[i];
                if (processor != null && !processor.UniversalPorts && processor.IsPowered && processor.IsProcessing)
                    total += MachinePower(world.Database.Machines[processor.MachineId].Key);
            }
            return total;
        }

        private int CalculateConfiguredPower()
        {
            if (driver == null || driver.World == null) return 0;
            int total = 0;
            var world = driver.World;
            for (int i = 0; i < world.Miners.Count; i++)
                if (world.Miners[i] != null) total += MachinePower(world.Database.Machines[world.Miners[i].MachineId].Key);
            for (int i = 0; i < world.Processors.Count; i++)
            {
                var processor = world.Processors[i];
                if (processor != null && !processor.UniversalPorts && processor.RecipeId >= 0)
                    total += MachinePower(world.Database.Machines[processor.MachineId].Key);
            }
            return total;
        }

        private static int MachinePower(string machineKey)
        {
            if (DataManager.Instance != null
                && DataManager.Instance.machineDict.TryGetValue(machineKey, out var machine))
                return Mathf.Max(0, machine.powerConsumption);
            return 0;
        }

        private static float Ratio(float actual, float optimal)
        {
            if (optimal <= 0.01f) return actual > 0.01f ? 1f : 0f;
            return Mathf.Clamp01(actual / optimal);
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize,
            FontStyle style, Vector2 position, Vector2 size)
        {
            var text = SeoUIFactory.CreateText(parent, name, value, fontSize, TextAnchor.UpperLeft, style);
            SeoUIFactory.SetRect(text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), position, size);
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
