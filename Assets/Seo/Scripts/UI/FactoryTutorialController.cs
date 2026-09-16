using System.Collections.Generic;
using Factory.Building;
using Factory.Buildings;
using Factory.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Seo.UI
{
    // 첫 생산 루프를 실제 게임 상태로 검증하는 런타임 튜토리얼.
    // 기존 건설/레시피/벨트 코드를 호출하거나 복제하지 않고, 플레이어가 만든 결과만 읽는다.
    public sealed class FactoryTutorialController : MonoBehaviour
    {
        private enum Step
        {
            Waiting,
            Core,
            PickMiner,
            PlaceMiner,
            InspectMiner,
            Mining,
            PickSmelter,
            PlaceSmelter,
            InspectSmelter,
            PickRecipe,
            ConnectInput,
            ConnectOutput,
            Complete,
        }

        private static readonly Vector2Int CopperCell = new Vector2Int(-4, 5);
        // 구리 광맥과 코어 사이의 빈 칸. 이 위치를 기준으로 입출력 포트 안내를 그린다.
        private static readonly Vector2Int SmelterCell = new Vector2Int(-3, 2);

        private SimulationDriver driver;
        private MachineGhostTool machineTool;
        private BuildInputRouter buildRouter;
        private FactoryHudController hud;
        private Step step = Step.Waiting;
        private Text titleText;
        private Text bodyText;
        private Text progressText;
        private Button skipButton;
        private GameObject panelRoot;
        private GameObject worldHighlight;
        private GameObject placementPreview;
        private Outline uiOutline;
        private Graphic uiGraphic;
        private int copperOreId = -1;
        private int copperIngotId = -1;
        private int copperRecipeId = -1;
        private int minerIndex = -1;
        private int smelterIndex = -1;
        private int initialCopperOre;
        private int initialCopperIngot;
        private float enteredAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRuntimeInstance()
        {
            if (FindFirstObjectByType<FactoryTutorialController>() != null) return;
            new GameObject("[Seo] Factory Tutorial").AddComponent<FactoryTutorialController>();
        }

        private void Update()
        {
            if (!Discover()) return;
            if (step == Step.Waiting) Begin();

            AnimateHighlights();
            EvaluateStep();
        }

        private bool Discover()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (machineTool == null) machineTool = FindFirstObjectByType<MachineGhostTool>();
            if (buildRouter == null) buildRouter = FindFirstObjectByType<BuildInputRouter>();
            if (hud == null) hud = FindFirstObjectByType<FactoryHudController>();
            if (driver == null || driver.World == null || machineTool == null || hud == null) return false;

            if (panelRoot == null)
            {
                var canvasObject = GameObject.Find("HUDCanvas");
                if (canvasObject == null) return false;
                BuildPanel(canvasObject.transform.Find("SafeArea") ?? canvasObject.transform);
            }
            return panelRoot != null;
        }

        private void Begin()
        {
            var db = driver.World.Database;
            if (!db.TryGetResourceId("CopperOre", out copperOreId)
                || !db.TryGetResourceId("CopperIngot", out copperIngotId)
                || !db.TryGetRecipeId("SmeltCopperIngot", out copperRecipeId))
            {
                Debug.LogWarning("[Tutorial] 구리 자원 또는 제련 레시피가 없어 튜토리얼을 시작하지 않습니다.");
                panelRoot.SetActive(false);
                enabled = false;
                return;
            }
            Enter(Step.Core);
        }

        private void EvaluateStep()
        {
            var world = driver.World;
            switch (step)
            {
                case Step.Core:
                    if (IsSelected(MachineInstanceKind.Processor, world.CoreProcessorIndex)) Enter(Step.PickMiner);
                    break;
                case Step.PickMiner:
                    if (machineTool.SelectedMachineId == "Miner") Enter(Step.PlaceMiner);
                    break;
                case Step.PlaceMiner:
                    if (TryGetOccupant(CopperCell, CellOccupantType.Miner, out minerIndex)) Enter(Step.InspectMiner);
                    break;
                case Step.InspectMiner:
                    if (IsSelected(MachineInstanceKind.Miner, minerIndex)) Enter(Step.Mining);
                    break;
                case Step.Mining:
                    if (CoreAmount(copperOreId) > initialCopperOre) Enter(Step.PickSmelter);
                    break;
                case Step.PickSmelter:
                    if (machineTool.SelectedMachineId == "Smelter") Enter(Step.PlaceSmelter);
                    break;
                case Step.PlaceSmelter:
                    if (TryGetOccupant(SmelterCell, CellOccupantType.Processor, out int placed)
                        && IsMachine(placed, "Smelter"))
                    {
                        smelterIndex = placed;
                        Enter(Step.InspectSmelter);
                    }
                    break;
                case Step.InspectSmelter:
                    if (IsSelected(MachineInstanceKind.Processor, smelterIndex)) Enter(Step.PickRecipe);
                    break;
                case Step.PickRecipe:
                    // 레시피 창이 열린 뒤에는 설정 버튼 대신 정확한 구리 레시피를 빛낸다.
                    if (GameObject.Find("Recipe_SmeltCopperIngot") != null
                        && (uiOutline == null || uiOutline.gameObject.name != "Recipe_SmeltCopperIngot"))
                    {
                        ClearHighlights();
                        HighlightUI("Recipe_SmeltCopperIngot");
                    }
                    if (ValidProcessor(smelterIndex) && world.Processors[smelterIndex].RecipeId == copperRecipeId)
                        Enter(Step.ConnectInput);
                    break;
                case Step.ConnectInput:
                    if (HasBeltPath(world.CoreProcessorIndex, smelterIndex)) Enter(Step.ConnectOutput);
                    break;
                case Step.ConnectOutput:
                    if (HasBeltPath(smelterIndex, world.CoreProcessorIndex)
                        && CoreAmount(copperIngotId) > initialCopperIngot)
                        Enter(Step.Complete);
                    break;
            }
        }

        private void Enter(Step next)
        {
            ClearHighlights();
            step = next;
            enteredAt = Time.unscaledTime;
            progressText.text = $"튜토리얼  {Mathf.Min((int)next, 12)} / 12";

            switch (next)
            {
                case Step.Core:
                    SetCopy("공장의 중심, 코어", "빛나는 코어를 클릭하세요. 코어는 채굴한 자원을 저장하고 생산 시설에 공급합니다.");
                    HighlightWorld(GameObject.Find("Core"), 2.8f);
                    break;
                case Step.PickMiner:
                    SetCopy("구리 채굴 준비", "빛나는 채굴기 버튼을 누르세요. 채굴기는 광맥 위에만 배치할 수 있습니다.");
                    hud.OpenProductionForTutorial();
                    HighlightUI("PaletteButton_Miner");
                    break;
                case Step.PlaceMiner:
                    SetCopy("구리 광맥에 배치", "빛나는 구리 광맥 위로 채굴기를 옮긴 뒤 배치 확정을 누르세요.");
                    HighlightCell(CopperCell, 1.25f);
                    break;
                case Step.InspectMiner:
                    CancelPlacementMode();
                    SetCopy("채굴기 정보 확인", "방금 설치한 채굴기를 클릭해 전력 소비량을 확인하세요.");
                    HighlightMachine(MachineInstanceKind.Miner, minerIndex, 1.6f);
                    break;
                case Step.Mining:
                    initialCopperOre = CoreAmount(copperOreId);
                    SetCopy("구리 채굴 중", "전력 소비와 채굴 진행률을 확인하세요. 구리 원석이 코어에 들어오면 자동으로 다음 단계로 진행합니다.");
                    HighlightNamedChild("MachineInfoPanel", "Power");
                    HighlightMachine(MachineInstanceKind.Miner, minerIndex, 1.6f);
                    break;
                case Step.PickSmelter:
                    SetCopy("제련로 건설", "빛나는 제련로 버튼을 누르세요.");
                    hud.OpenProductionForTutorial();
                    HighlightUI("PaletteButton_Smelter");
                    break;
                case Step.PlaceSmelter:
                    SetCopy("예시 위치에 배치", "반투명 제련로가 표시된 칸에 실제 제련로를 배치하세요.");
                    CreatePlacementPreview();
                    HighlightCell(SmelterCell, 1.4f);
                    break;
                case Step.InspectSmelter:
                    CancelPlacementMode();
                    SetCopy("제련로 설정", "배치한 제련로를 클릭하세요.");
                    HighlightMachine(MachineInstanceKind.Processor, smelterIndex, 1.6f);
                    break;
                case Step.PickRecipe:
                    SetCopy("구리 제련 레시피", "레시피 설정을 누르고 ‘구리 원석 → 구리 괴’를 선택하세요.");
                    HighlightNamedChild("MachineInfoPanel", "RecipeButton");
                    break;
                case Step.ConnectInput:
                    SetCopy("코어 → 제련로", "물류의 벨트를 선택하고 코어에서 제련로 입력 포트까지 연결하세요.");
                    hud.OpenLogisticsForTutorial();
                    HighlightUI("PaletteButton_Belt");
                    HighlightCell(GetSmelterPort(false), 0.72f);
                    break;
                case Step.ConnectOutput:
                    initialCopperIngot = CoreAmount(copperIngotId);
                    SetCopy("제련로 → 코어", "제련로 출력 포트에서 코어까지 벨트를 연결하세요. 구리 괴가 코어에 도착하면 완료됩니다.");
                    hud.OpenLogisticsForTutorial();
                    HighlightUI("PaletteButton_Belt");
                    HighlightCell(GetSmelterPort(true), 0.72f);
                    break;
                case Step.Complete:
                    SetCopy("생산 라인 완성!", "구리 채굴부터 제련, 코어 저장까지 자동 생산 라인이 완성되었습니다.");
                    progressText.text = "튜토리얼 완료";
                    skipButton.GetComponentInChildren<Text>().text = "닫기";
                    skipButton.onClick.RemoveAllListeners();
                    skipButton.onClick.AddListener(() => panelRoot.SetActive(false));
                    break;
            }
        }

        private void BuildPanel(Transform parent)
        {
            var panel = SeoUIFactory.CreatePanel(parent, "FactoryTutorialPanel", new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -186f), new Vector2(720f, 170f),
                new Color(0.015f, 0.08f, 0.12f, 0.97f));
            panel.rectTransform.pivot = new Vector2(0.5f, 1f);
            panelRoot = panel.gameObject;

            progressText = SeoUIFactory.CreateText(panel.transform, "Progress", "튜토리얼", 17,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(progressText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(420f, 26f));
            progressText.color = SeoUITheme.Current.Primary;

            titleText = SeoUIFactory.CreateText(panel.transform, "Title", string.Empty, 26,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SeoUIFactory.SetRect(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -42f), new Vector2(550f, 38f));

            bodyText = SeoUIFactory.CreateText(panel.transform, "Description", string.Empty, 19,
                TextAnchor.UpperLeft);
            SeoUIFactory.SetRect(bodyText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(24f, -84f), new Vector2(560f, 70f));

            skipButton = SeoUIFactory.CreateButton(panel.transform, "Skip", "건너뛰기", SkipTutorial,
                SeoUITheme.Current.Danger);
            SeoUIFactory.SetRect(skipButton.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-18f, -18f), new Vector2(112f, 50f));
            panel.transform.SetAsLastSibling();
        }

        private void SetCopy(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
        }

        private void SkipTutorial()
        {
            ClearHighlights();
            panelRoot.SetActive(false);
            enabled = false;
        }

        private void CancelPlacementMode()
        {
            if (machineTool != null) machineTool.CancelPlacement();
            if (buildRouter != null && buildRouter.CurrentMode == BuildInputRouter.Mode.PlaceMachine)
                buildRouter.SetMode(BuildInputRouter.Mode.None);
        }

        private void HighlightUI(string objectName)
        {
            var target = GameObject.Find(objectName);
            if (target == null) return;
            uiGraphic = target.GetComponent<Graphic>();
            if (uiGraphic == null) uiGraphic = target.GetComponentInChildren<Graphic>(true);
            if (uiGraphic == null) return;
            uiOutline = uiGraphic.gameObject.AddComponent<Outline>();
            uiOutline.effectDistance = new Vector2(6f, -6f);
            uiOutline.useGraphicAlpha = false;
        }

        private void HighlightNamedChild(string rootName, string childName)
        {
            var root = GameObject.Find(rootName);
            if (root == null) return;
            var child = FindDeepChild(root.transform, childName);
            if (child != null) HighlightUI(child.gameObject.name);
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == childName) return all[i];
            return null;
        }

        private void HighlightMachine(MachineInstanceKind kind, int index, float radius)
        {
            HighlightWorld(GameObject.Find($"{kind}_{index}"), radius);
        }

        private void HighlightWorld(GameObject target, float radius)
        {
            if (target == null) return;
            worldHighlight = CreateRing(target.transform.position, radius);
            worldHighlight.transform.SetParent(target.transform, true);
        }

        private void HighlightCell(Vector2Int cell, float radius)
        {
            Vector3 position = GridUtility.CellToWorldCenter(cell, 0.06f);
            worldHighlight = CreateRing(position, radius);
        }

        private static GameObject CreateRing(Vector3 position, float radius)
        {
            var go = new GameObject("TutorialWorldHighlight");
            go.transform.position = position;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 65;
            line.widthMultiplier = 0.09f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = new Color(0.1f, 1f, 0.9f, 0.95f);
            for (int i = 0; i < line.positionCount; i++)
            {
                float a = Mathf.PI * 2f * i / (line.positionCount - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            return go;
        }

        private void CreatePlacementPreview()
        {
            placementPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placementPreview.name = "TutorialSmelterPreview";
            placementPreview.transform.position = GridUtility.CellToWorldCenter(SmelterCell, 0.5f);
            placementPreview.transform.localScale = new Vector3(0.88f, 1f, 0.88f);
            Destroy(placementPreview.GetComponent<Collider>());
            var renderer = placementPreview.GetComponent<Renderer>();
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = new Color(0.15f, 0.95f, 1f, 0.28f);
            renderer.material = material;

            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(placementPreview.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            label.transform.localScale = Vector3.one * 0.12f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 44;
            label.color = new Color(0.3f, 1f, 1f, 0.95f);
            label.text = "제련로\n배치 위치";
        }

        private void AnimateHighlights()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            if (uiOutline != null)
            {
                uiOutline.effectColor = Color.Lerp(new Color(0f, 0.65f, 1f, 0.65f), Color.white, pulse);
                uiOutline.effectDistance = Vector2.one * Mathf.Lerp(3f, 8f, pulse);
            }
            if (worldHighlight != null)
            {
                var line = worldHighlight.GetComponent<LineRenderer>();
                if (line != null)
                {
                    Color color = Color.Lerp(new Color(0f, 0.65f, 1f, 0.45f), new Color(0.2f, 1f, 0.85f, 1f), pulse);
                    line.startColor = line.endColor = color;
                    line.widthMultiplier = Mathf.Lerp(0.06f, 0.16f, pulse);
                }
            }
            if (placementPreview != null)
                placementPreview.transform.localScale = Vector3.one * Mathf.Lerp(0.86f, 0.94f, pulse);
        }

        private void ClearHighlights()
        {
            if (uiOutline != null) Destroy(uiOutline);
            if (worldHighlight != null) Destroy(worldHighlight);
            if (placementPreview != null) Destroy(placementPreview);
            uiOutline = null;
            uiGraphic = null;
            worldHighlight = null;
            placementPreview = null;
        }

        private bool IsSelected(MachineInstanceKind kind, int index)
        {
            var ui = UIManager.Instance;
            return ui != null && ui.HasSelection && ui.IsMachineInfoOpen
                && ui.SelectedKind == kind && ui.SelectedIndex == index;
        }

        private bool TryGetOccupant(Vector2Int cell, CellOccupantType type, out int index)
        {
            index = -1;
            if (!driver.World.Grid.TryGetOccupant(cell, out var occupant) || occupant.Type != type) return false;
            index = occupant.InstanceIndex;
            return true;
        }

        private bool IsMachine(int processorIndex, string machineKey)
        {
            if (!ValidProcessor(processorIndex)) return false;
            var processor = driver.World.Processors[processorIndex];
            return driver.World.Database.Machines[processor.MachineId].Key == machineKey;
        }

        private bool ValidProcessor(int index)
        {
            return index >= 0 && index < driver.World.Processors.Count && driver.World.Processors[index] != null;
        }

        private int CoreAmount(int resourceId)
        {
            int coreIndex = driver.World.CoreProcessorIndex;
            if (!ValidProcessor(coreIndex) || resourceId < 0) return 0;
            return driver.World.Processors[coreIndex].InputBuffer[resourceId];
        }

        private Vector2Int GetSmelterPort(bool output)
        {
            if (!ValidProcessor(smelterIndex)) return SmelterCell + (output ? Vector2Int.right : Vector2Int.left);
            var processor = driver.World.Processors[smelterIndex];
            return processor.Anchor + (output ? processor.Facing : -processor.Facing);
        }

        private bool HasBeltPath(int sourceProcessor, int targetProcessor)
        {
            var segments = driver.World.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                var start = segments[i];
                if (start == null || start.SourceProcessorId != sourceProcessor) continue;
                var visited = new HashSet<int>();
                var current = start;
                while (current != null && visited.Add(current.Id))
                {
                    if (current.TargetProcessorId == targetProcessor) return true;
                    if (!current.NextSegmentId.HasValue) break;
                    int next = current.NextSegmentId.Value;
                    current = next >= 0 && next < segments.Count ? segments[next] : null;
                }
            }
            return false;
        }
    }
}
