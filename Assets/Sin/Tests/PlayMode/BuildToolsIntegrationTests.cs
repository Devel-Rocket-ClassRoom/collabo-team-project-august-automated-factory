using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

// BeltDragTool/MachineGhostTool을 실제 화면 좌표(오소그래픽 탑다운 카메라 기준)로 조작해서
// 사용자가 보고한 시나리오(벨트에 붙여서 기계 설치, 떨어뜨려 설치 후 벨트로 연결, 벨트를
// 따로따로 그려서 잇기)를 그대로 재현하고 실제로 아이템이 끝까지 도착하는지 검증한다.
//
// 채굴기는 입출력 포트가 없다(원격 전송, MinerSystem 참고) — 그래서 벨트 체인 자체를
// 검증하는 테스트들은 "항상 존재하는 저장고"인 코어(UniversalPorts Processor)를 소스로 쓴다.
public class BuildToolsIntegrationTests
{
    private static readonly Vector2 GhostScreenOffset = new Vector2(0f, 150f);

    private GameObject cameraGO;
    private GameObject driverGO;
    private GameObject toolsGO;
    private Camera cam;
    private SimulationDriver driver;
    private BeltDragTool beltTool;
    private MachineGhostTool machineTool;

    private ResourceDef oreDef;
    private ResourceDef outputDef;
    private MachineDef minerDef;
    private MachineDef processorDef;
    private RecipeDef recipeDef;

    [SetUp]
    public void SetUp()
    {
        oreDef = ScriptableObject.CreateInstance<ResourceDef>();
        oreDef.resourceId = "TestOre";

        outputDef = ScriptableObject.CreateInstance<ResourceDef>();
        outputDef.resourceId = "TestOutput";

        minerDef = ScriptableObject.CreateInstance<MachineDef>();
        minerDef.machineId = "TestMiner";
        minerDef.category = MachineCategory.Miner;
        minerDef.minerOutput = oreDef;

        processorDef = ScriptableObject.CreateInstance<MachineDef>();
        processorDef.machineId = "TestProcessor";
        processorDef.category = MachineCategory.Smelter;

        recipeDef = ScriptableObject.CreateInstance<RecipeDef>();
        recipeDef.recipeId = "TestRecipe";
        recipeDef.inputs = new[] { new RecipeIngredient { resource = oreDef, amount = 1 } };
        // 출력을 둬서 "누적 생산량"으로 아이템이 끝까지 도착했는지 명확하게 판정한다
        // (InputBuffer는 소비되는 순간 0으로 돌아가서 타이밍에 따라 애매해짐).
        recipeDef.outputs = new[] { new RecipeIngredient { resource = outputDef, amount = 1 } };
        recipeDef.processSeconds = 0.1f;
        recipeDef.requiredCategory = MachineCategory.Smelter;

        var db = GameDatabase.Build(new[] { oreDef, outputDef }, new[] { recipeDef }, new[] { minerDef, processorDef });
        db.MakeGlobal();

        cameraGO = new GameObject("TestCamera");
        cam = cameraGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cameraGO.transform.position = new Vector3(0f, 15f, 0f);
        cameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        driverGO = new GameObject("TestDriver");
        driver = driverGO.AddComponent<SimulationDriver>();

        toolsGO = new GameObject("TestTools");
        beltTool = toolsGO.AddComponent<BeltDragTool>();
        beltTool.Initialize(cam, driver);
        machineTool = toolsGO.AddComponent<MachineGhostTool>();
        machineTool.Initialize(cam, driver);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(cameraGO);
        Object.DestroyImmediate(driverGO);
        Object.DestroyImmediate(toolsGO);
        Object.DestroyImmediate(oreDef);
        Object.DestroyImmediate(outputDef);
        Object.DestroyImmediate(minerDef);
        Object.DestroyImmediate(processorDef);
        Object.DestroyImmediate(recipeDef);
    }

    private Vector2 ScreenPosForCell(Vector2Int cell)
    {
        return cam.WorldToScreenPoint(GridUtility.CellToWorldCenter(cell, 0.5f));
    }

    private void PlaceMachine(MachineDef def, Vector2Int cell)
    {
        machineTool.SelectMachine(def);
        machineTool.OnPressBegin(ScreenPosForCell(cell) - GhostScreenOffset);
        bool confirmed = machineTool.Confirm();
        Assert.IsTrue(confirmed, $"{def.machineId} 배치가 {cell}에서 실패함");
    }

    // 레시피는 이제 배치 시 자동 배정되지 않고 탭-선택 UI로 고른다 (RecipeSelectionPanel).
    // 이 테스트들은 벨트 연결 자체를 검증하는 거라 UI 클릭 대신 직접 대입해서 "플레이어가
    // 방금 고른 것"을 흉내낸다.
    private void AssignRecipeToNewestProcessor()
    {
        int recipeId = driver.World.Database.GetRecipeId("TestRecipe");
        driver.World.Processors[driver.World.Processors.Count - 1].RecipeId = recipeId;
    }

    // CoreSpawner와 동일한 방식으로(RecipeId=-1, UniversalPorts=true) 코어를 흉내낸 Processor를
    // 등록한다. 채굴기가 원격 전송으로 넣어주는 대상이자, 벨트 체인 테스트의 "항상 있는 소스".
    private ProcessorInstance PlaceCoreLike(Vector2Int anchor, Vector2Int footprint)
    {
        var cells = GridUtility.GetFootprintCells(anchor, footprint);
        var core = new ProcessorInstance(driver.World.Database.ResourceCount) { MachineId = -1, RecipeId = -1, UniversalPorts = true };
        int index = driver.World.AddProcessor(core);
        driver.World.CoreProcessorIndex = index;
        driver.World.Grid.RegisterBuildingFootprint(cells, CellOccupantType.Processor, index);
        return core;
    }

    private void DragBelt(Vector2Int from, Vector2Int to)
    {
        beltTool.OnPressBegin(ScreenPosForCell(from));
        beltTool.OnDrag(ScreenPosForCell(to));
        beltTool.OnReleased(ScreenPosForCell(to));
    }

    private void RunTicks(int count, float delta = 0.05f)
    {
        for (int i = 0; i < count; i++) driver.World.Tick(delta);
    }

    [Test]
    public void PlacingMachine_AdjacentToExistingDeadEndBelt_AutoConnects()
    {
        // "벨트를 먼저 뻗어두고 나중에 옆에 기계를 놓는" 시나리오. 소스는 코어(항상 존재하는
        // 저장고)로 — 채굴기는 벨트에 연결될 수 없다(원격 전송).
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        DragBelt(new Vector2Int(0, 0), new Vector2Int(2, 0)); // 코어 옆에서 (2,0)까지 뻗어놓고 방치(막다른 길)

        PlaceMachine(processorDef, new Vector2Int(3, 0)); // 벨트 끝(2,0) 바로 옆에 나중에 배치
        AssignRecipeToNewestProcessor();

        RunTicks(400); // 20초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "코어 옆에서 뻗은 벨트가 나중에 놓인 제련로까지 실제로 아이템을 날라야 함");
    }

    [Test]
    public void ConnectingTwoSeparatelyDrawnBeltSegments_ItemsFlowThroughJoint()
    {
        // "컨베이어끼리 따로 그려서 연결" 시나리오: 한 번에 쭉 드래그하지 않고 두 번에 나눠서 그림.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        PlaceMachine(processorDef, new Vector2Int(6, 0));
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // 1차: 코어 -> 중간까지
        DragBelt(new Vector2Int(3, 0), new Vector2Int(6, 0)); // 2차: 이어서 -> 제련로까지 (별도 드래그)

        RunTicks(600); // 30초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "따로 그린 두 벨트 구간의 연결부에서 아이템이 막히지 않고 제련로까지 도착해야 함");
    }

    [Test]
    public void PlacingMachine_WithGap_ThenBridgingBelt_ConnectsAndTransports()
    {
        // 기계를 벨트와 떨어뜨려 설치한 뒤, 그 사이를 잇는 벨트를 그리는 시나리오.
        var core = PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        core.InputBuffer[driver.World.Database.GetResourceId("TestOre")] = 20;

        PlaceMachine(processorDef, new Vector2Int(5, 0)); // 미리 좀 떨어진 곳에 배치
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(5, 0)); // 그 사이를 잇는 벨트

        RunTicks(500);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "떨어뜨려 설치한 기계 사이를 잇는 벨트로 아이템이 도착해야 함");
    }

    [Test]
    public void DraggingIntoMachineSideFace_DoesNotConnect()
    {
        // 고정 포트 회귀 테스트: 기본 Facing (1,0)인 제련로는 입력이 (-1,0) 방향, 출력이
        // (1,0) 방향뿐이다. 측면((0,1) 방향)에서 드래그해 붙여도 연결되면 안 된다.
        PlaceMachine(processorDef, new Vector2Int(0, 0));

        DragBelt(new Vector2Int(0, 3), new Vector2Int(0, 0)); // 북쪽(측면)에서 기계 칸으로 바로 드래그

        Assert.AreEqual(0, driver.World.Segments.Count, "측면에는 포트가 없으니 아무 세그먼트도 만들어지면 안 됨");
    }

    [Test]
    public void PlacingBeltOntoMiner_FromAnySide_NeverConnects()
    {
        // 채굴기는 입출력 포트가 없다(원격 전송) — 어느 면에서 드래그해도 벨트가 연결되면 안 된다.
        PlaceMachine(minerDef, new Vector2Int(0, 0));

        DragBelt(new Vector2Int(0, 0), new Vector2Int(1, 0)); // 동쪽으로
        DragBelt(new Vector2Int(0, 0), new Vector2Int(-1, 0)); // 서쪽으로
        DragBelt(new Vector2Int(0, 0), new Vector2Int(0, 1)); // 북쪽으로
        DragBelt(new Vector2Int(-1, 0), new Vector2Int(0, 0)); // 반대 방향(채굴기로 들어오는 드래그)

        Assert.AreEqual(0, driver.World.Segments.Count, "채굴기는 어느 면으로 드래그해도 벨트가 연결되면 안 됨");
    }

    [Test]
    public void FullLoop_MinerDeliversToCore_BeltCarriesToProcessor_NoMinerBelt()
    {
        // 실제 플레이 순서 그대로: 채굴기를 놓으면(벨트 없이) 자동으로 코어에 쌓이고, 코어에서
        // 뻗은 벨트가 제련로까지 실어 날라서 최종 산출물이 나와야 한다.
        PlaceCoreLike(new Vector2Int(0, 0), new Vector2Int(1, 1));
        PlaceMachine(minerDef, new Vector2Int(4, 4)); // 코어와 멀리 떨어진 곳 — 벨트로 안 이어도 됨
        PlaceMachine(processorDef, new Vector2Int(3, 0));
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0));

        RunTicks(1000); // 채굴 + 원격 전송 + 벨트 이동 + 제련까지 충분한 시간

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "채굴 -> 코어 원격 전송 -> 벨트 -> 제련까지 전체 루프가 실제로 동작해야 함");
    }

    [Test]
    public void CoreLikeUniversalPortsProcessor_SuppliesStoredResource_ToRequestingProcessor()
    {
        // 코어 재현: RecipeId=-1(아무것도 생산 안 함) + UniversalPorts=true(4면 다 포트)인
        // ProcessorInstance를 2x2 칸에 등록해두고(CoreSpawner와 동일한 등록 방식), 그 칸에서
        // 시작하는 드래그로 벨트를 그으면 Source로 잡혀서 쌓아둔 자원을 실제로 넘겨줘야 한다.
        var core = PlaceCoreLike(new Vector2Int(-1, -1), new Vector2Int(2, 2));
        int coreIndex = driver.World.Processors.IndexOf(core);

        int oreId = driver.World.Database.GetResourceId("TestOre");
        core.InputBuffer[oreId] = 5; // 미리 쌓여있는 재고

        PlaceMachine(processorDef, new Vector2Int(2, 0)); // 코어 동쪽 모서리(0,-1)/(0,0)에서 두 칸 떨어진 곳
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(0, 0), new Vector2Int(2, 0)); // 코어 칸(0,0)에서 시작 -> 제련로 입력면까지

        Assert.AreEqual(1, driver.World.Segments.Count, "코어 칸에서 시작한 드래그는 벨트로 연결되어야 함");
        Assert.AreEqual(coreIndex, driver.World.Segments[0].SourceProcessorId, "코어가 벨트의 소스로 잡혀야 함");

        RunTicks(400);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "코어에 쌓인 재고가 벨트를 타고 제련로까지 도착해서 실제로 소비/가공되어야 함");
    }

    [Test]
    public void DraggingFromProcessorInputTowardCore_StillResolvesCoreAsSource()
    {
        // 실제 플레이에서 자연스러운 조작 순서: 코어에서부터가 아니라, 방금 놓은 제련로를
        // 먼저 누르고 거기서부터 코어 쪽으로 드래그하는 경우. 제련로 입력면 쪽을 만지므로
        // 그 끝은 Target으로 고정 판정되는데, 코어 쪽 끝은 예전 코드에서 isStart가 아니라는
        // 이유만으로 똑같이 Target으로 잡혀서(=둘 다 Target) 방향이 뒤바뀌어 버리던 버그.
        var coreCell = new Vector2Int(1, 0);
        var core = PlaceCoreLike(coreCell, new Vector2Int(1, 1));
        int coreIndex = driver.World.Processors.IndexOf(core);

        int oreId = driver.World.Database.GetResourceId("TestOre");
        core.InputBuffer[oreId] = 5;

        PlaceMachine(processorDef, new Vector2Int(4, 0)); // Facing 기본값 (1,0) -> 입력면은 (3,0)
        AssignRecipeToNewestProcessor();

        DragBelt(new Vector2Int(4, 0), coreCell); // 제련로에서 시작 -> 코어 쪽으로 드래그(역방향)

        Assert.AreEqual(2, driver.World.Segments.Count, "역방향으로 드래그해도 유효한 포트끼리면 연결되어야 함(코어와 제련로 사이 두 칸)");
        Assert.AreEqual(coreIndex, driver.World.Segments[0].SourceProcessorId, "코어가 소스로 잡혀야 함(제련로가 아니라)");

        var smelter = driver.World.Processors[driver.World.Processors.Count - 1];
        int smelterIndex = driver.World.Processors.IndexOf(smelter);
        Assert.AreEqual(smelterIndex, driver.World.Segments[driver.World.Segments.Count - 1].TargetProcessorId, "제련로가 타겟으로 잡혀야 함");

        RunTicks(400);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(smelter.OutputBuffer[outputId], 0,
            "역방향 드래그로 이어도 코어의 재고가 실제로 제련로까지 도착해야 함");
    }
}
