using Factory.Building;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

// BeltDragTool/MachineGhostTool을 실제 화면 좌표(오소그래픽 탑다운 카메라 기준)로 조작해서
// 사용자가 보고한 시나리오(벨트에 붙여서 기계 설치, 떨어뜨려 설치 후 벨트로 연결, 벨트를
// 따로따로 그려서 잇기)를 그대로 재현하고 실제로 아이템이 끝까지 도착하는지 검증한다.
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
        // "벨트를 먼저 뻗어두고 나중에 옆에 기계를 놓는" 시나리오.
        PlaceMachine(minerDef, new Vector2Int(0, 0));
        DragBelt(new Vector2Int(0, 0), new Vector2Int(2, 0)); // 채굴기 옆에서 (2,0)까지 뻗어놓고 방치(막다른 길)

        PlaceMachine(processorDef, new Vector2Int(3, 0)); // 벨트 끝(2,0) 바로 옆에 나중에 배치

        Assert.AreEqual(1, driver.World.Miners.Count);
        Assert.AreEqual(1, driver.World.Processors.Count);

        RunTicks(400); // 20초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(driver.World.Processors[0].OutputBuffer[outputId], 0,
            "채굴기 옆에서 뻗은 벨트가 나중에 놓인 제련로까지 실제로 아이템을 날라야 함");
    }

    [Test]
    public void ConnectingTwoSeparatelyDrawnBeltSegments_ItemsFlowThroughJoint()
    {
        // "컨베이어끼리 따로 그려서 연결" 시나리오: 한 번에 쭉 드래그하지 않고 두 번에 나눠서 그림.
        PlaceMachine(minerDef, new Vector2Int(0, 0));
        PlaceMachine(processorDef, new Vector2Int(6, 0));

        DragBelt(new Vector2Int(0, 0), new Vector2Int(3, 0)); // 1차: 채굴기 -> 중간까지
        DragBelt(new Vector2Int(3, 0), new Vector2Int(6, 0)); // 2차: 이어서 -> 제련로까지 (별도 드래그)

        RunTicks(600); // 30초 분량

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(driver.World.Processors[0].OutputBuffer[outputId], 0,
            "따로 그린 두 벨트 구간의 연결부에서 아이템이 막히지 않고 제련로까지 도착해야 함");
    }

    [Test]
    public void PlacingMachine_WithGap_ThenBridgingBelt_ConnectsAndTransports()
    {
        // 기계를 벨트와 떨어뜨려 설치한 뒤, 그 사이를 잇는 벨트를 그리는 시나리오.
        PlaceMachine(minerDef, new Vector2Int(0, 0));
        PlaceMachine(processorDef, new Vector2Int(5, 0)); // 미리 좀 떨어진 곳에 배치

        DragBelt(new Vector2Int(0, 0), new Vector2Int(5, 0)); // 그 사이를 잇는 벨트

        RunTicks(500);

        int outputId = driver.World.Database.GetResourceId("TestOutput");
        Assert.Greater(driver.World.Processors[0].OutputBuffer[outputId], 0,
            "떨어뜨려 설치한 기계 사이를 잇는 벨트로 아이템이 도착해야 함");
    }
}
