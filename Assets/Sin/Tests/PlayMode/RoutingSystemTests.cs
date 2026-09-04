using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

public class RoutingSystemTests
{
    [Test]
    public void Splitter_DistributesEvenlyAcrossThreeOutputs_RoundRobin()
    {
        var db = BuildDatabase(out int oreId, out _);
        var splitter = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter };
        splitter.InputBuffer[oreId] = 30;

        var outA = new BeltSegment { Id = 0, SourceProcessorId = 0 };
        var outB = new BeltSegment { Id = 1, SourceProcessorId = 0 };
        var outC = new BeltSegment { Id = 2, SourceProcessorId = 0 };

        var processors = new List<ProcessorInstance> { splitter };
        var segments = new List<BeltSegment> { outA, outB, outC };
        var system = new RoutingSystem();

        int a = 0, b = 0, c = 0;
        for (int i = 0; i < 30; i++)
        {
            system.Tick(processors, segments);
            // 벨트가 곧바로 실어 갔다고 치고 입구를 비운다(다음 틱에 또 받을 수 있게).
            a += Drain(outA);
            b += Drain(outB);
            c += Drain(outC);
        }

        Assert.AreEqual(30, a + b + c, "투입 30개가 전부 세 출력으로 나뉘어야 함");
        Assert.AreEqual(10, a);
        Assert.AreEqual(10, b);
        Assert.AreEqual(10, c);
        Assert.AreEqual(0, splitter.InputBuffer[oreId]);
    }

    [Test]
    public void Splitter_SkipsBlockedOutput_AndKeepsFlowingToTheRest()
    {
        var db = BuildDatabase(out int oreId, out _);
        var splitter = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter };
        splitter.InputBuffer[oreId] = 20;

        var outA = new BeltSegment { Id = 0, SourceProcessorId = 0 };
        var outBlocked = new BeltSegment { Id = 1, SourceProcessorId = 0 };
        var outC = new BeltSegment { Id = 2, SourceProcessorId = 0 };
        // outBlocked 입구에 아이템을 박아두고 절대 안 비운다 -> 항상 HeadFree=false -> 건너뛰어야 함.
        outBlocked.Items.Add(new BeltItem(oreId, 0f));

        var processors = new List<ProcessorInstance> { splitter };
        var segments = new List<BeltSegment> { outA, outBlocked, outC };
        var system = new RoutingSystem();

        int a = 0, c = 0;
        for (int i = 0; i < 20; i++)
        {
            system.Tick(processors, segments);
            a += Drain(outA);
            c += Drain(outC);
        }

        Assert.AreEqual(1, outBlocked.Items.Count, "막힌 출력에는 아무것도 새로 안 들어가야 함");
        Assert.AreEqual(20, a + c, "나머지 두 출력으로 전량이 흘러야 함(전체 정지 아님)");
        Assert.AreEqual(0, splitter.InputBuffer[oreId]);
    }

    [Test]
    public void Merger_CombinesInputsIntoOneOutput_AlternatingResourceTypes()
    {
        var db = BuildDatabase(out int oreId, out int coalId);
        var merger = new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Merger };
        // 여러 입력 벨트가 배달해준 상태를 흉내 — InputBuffer에 두 종류가 쌓여 있다.
        merger.InputBuffer[oreId] = 15;
        merger.InputBuffer[coalId] = 15;

        var output = new BeltSegment { Id = 0, SourceProcessorId = 0 };

        var processors = new List<ProcessorInstance> { merger };
        var segments = new List<BeltSegment> { output };
        var system = new RoutingSystem();

        int ore = 0, coal = 0;
        int alternations = 0;
        int lastResource = -1;
        for (int i = 0; i < 30; i++)
        {
            system.Tick(processors, segments);
            if (output.Items.Count == 0) continue;
            int r = output.Items[0].ResourceId;
            if (r == oreId) ore++;
            else coal++;
            if (lastResource != -1 && r != lastResource) alternations++;
            lastResource = r;
            output.Items.Clear();
        }

        Assert.AreEqual(30, ore + coal, "두 입력 재고 30개가 전부 단일 출력으로 병합되어야 함");
        Assert.AreEqual(15, ore);
        Assert.AreEqual(15, coal);
        Assert.GreaterOrEqual(alternations, 28, "한 종류만 몰아 내보내지 않고 번갈아 내보내야 함");
    }

    [Test]
    public void CoreToSplitterToMachine_DispensesOnlyWhatTheDownstreamMachineNeeds()
    {
        // 사용자 보고: 코어 -> 벨트 -> 분류기 -> 기계로 이으면 아무것도 안 나왔다.
        // FindTerminalTarget이 분류기(RecipeId<0)에서 멈춰 코어가 뭘 원하는지 판단 못 했기 때문.
        // 이제 분류기를 통과해 뒤의 실제 기계 레시피를 보고 그 자원만 실어준다.
        var db = BuildChainDatabase(out int ironId, out int coalId, out int plateId, out int formRecipeId, out _);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 20;
        core.InputBuffer[coalId] = 20; // 레시피가 안 쓰는 자원 — 그대로 남아 있어야 함

        int splitterIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Splitter });
        var former = new ProcessorInstance(db.ResourceCount) { RecipeId = formRecipeId };
        int formerIndex = world.AddProcessor(former);

        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = splitterIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = splitterIndex, TargetProcessorId = formerIndex });

        for (int i = 0; i < 600; i++) world.Tick(0.05f);

        Assert.Greater(former.OutputBuffer[plateId], 0, "코어 -> 분류기 -> 성형기 체인에서 철판이 나와야 함");
        Assert.AreEqual(20, core.InputBuffer[coalId], "레시피가 안 쓰는 석탄은 분류기 너머로도 안 실려야 함");
    }

    [Test]
    public void CoreToMergerToTwoInputMachine_CombinesBothMaterialsIntoOnePort()
    {
        // 합류기가 종류별로 섞어 한 벨트에 실은 걸, 입력 2개짜리 기계가 한 포트로 받아도
        // 두 재료가 InputBuffer에 쌓여 레시피가 돈다.
        var db = BuildChainDatabase(out int ironId, out int coalId, out _, out _, out int synthRecipeId);
        int steelId = db.GetResourceId("SteelIngot");
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;
        core.InputBuffer[ironId] = 20;
        core.InputBuffer[coalId] = 20;

        int mergerIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount) { RoutingRole = RoutingRole.Merger });
        var synth = new ProcessorInstance(db.ResourceCount) { RecipeId = synthRecipeId };
        int synthIndex = world.AddProcessor(synth);

        // 코어 -> 합류기 두 라인(각자 철/석탄 담당 자동 배정), 합류기 -> 합성기 한 라인.
        world.AddBeltSegment(new BeltSegment { Id = 0, SourceProcessorId = coreIndex, TargetProcessorId = mergerIndex });
        world.AddBeltSegment(new BeltSegment { Id = 1, SourceProcessorId = coreIndex, TargetProcessorId = mergerIndex });
        world.AddBeltSegment(new BeltSegment { Id = 2, SourceProcessorId = mergerIndex, TargetProcessorId = synthIndex });

        for (int i = 0; i < 800; i++) world.Tick(0.05f);

        Assert.Greater(synth.OutputBuffer[steelId], 0, "합류기가 섞어 보낸 철+석탄을 합성기가 한 포트로 받아 강철 주괴를 만들어야 함");
    }

    private static int Drain(BeltSegment belt)
    {
        int n = belt.Items.Count;
        belt.Items.Clear();
        return n;
    }

    // 철광석/철주괴/석탄/철판/강철주괴 + 성형(철주괴->철판) + 합성(철주괴+석탄->강철주괴).
    private static GameDatabase BuildChainDatabase(out int ironId, out int coalId, out int plateId, out int formRecipeId, out int synthRecipeId)
    {
        var items = new[]
        {
            new ItemData { itemID = "IronIngot" },
            new ItemData { itemID = "Coal" },
            new ItemData { itemID = "IronPlate" },
            new ItemData { itemID = "SteelIngot" },
        };
        var machines = new[]
        {
            new MachineData { machineID = "Former" },
            new MachineData { machineID = "Synthesizer" },
        };
        var recipes = new[]
        {
            new RecipeData
            {
                recipeID = "FormIronPlate", machineID = "Former", timeToCraft = 1f,
                inputItems = new List<string> { "IronIngot" }, outputItems = new List<string> { "IronPlate" },
            },
            new RecipeData
            {
                recipeID = "SynthesizeSteelIngot", machineID = "Synthesizer", timeToCraft = 1f,
                inputItems = new List<string> { "IronIngot", "Coal" }, outputItems = new List<string> { "SteelIngot" },
            },
        };

        var db = GameDatabase.Build(items, machines, recipes);
        ironId = db.GetResourceId("IronIngot");
        coalId = db.GetResourceId("Coal");
        plateId = db.GetResourceId("IronPlate");
        formRecipeId = db.GetRecipeId("FormIronPlate");
        synthRecipeId = db.GetRecipeId("SynthesizeSteelIngot");
        return db;
    }

    private static GameDatabase BuildDatabase(out int oreId, out int coalId)
    {
        var ore = new ItemData { itemID = "IronOre" };
        var coal = new ItemData { itemID = "Coal" };
        var db = GameDatabase.Build(new[] { ore, coal }, System.Array.Empty<MachineData>(), System.Array.Empty<RecipeData>());
        oreId = db.GetResourceId("IronOre");
        coalId = db.GetResourceId("Coal");
        return db;
    }
}
