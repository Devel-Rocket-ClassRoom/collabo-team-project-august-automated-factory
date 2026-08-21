using System.Collections.Generic;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

public class BeltSystemTests
{
    [Test]
    public void Tick_MovesItemAlongSegment()
    {
        var segment = new BeltSegment { Id = 0, Length = 2f, SpeedUnitsPerSecond = 1f };
        segment.Items.Add(new BeltItem(0, 0f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.5f, segments, processors, null);
        Assert.AreEqual(0.5f, segment.Items[0].Position, 0.001f);

        system.Tick(0.5f, segments, processors, null);
        Assert.AreEqual(1.0f, segment.Items[0].Position, 0.001f);
    }

    [Test]
    public void Tick_ItemsNeverOvertake_AndCompressFromBehindWhenBlocked()
    {
        var segment = new BeltSegment { Id = 0, Length = 5f, SpeedUnitsPerSecond = 10f, ItemSpacing = 0.3f };
        segment.Items.Add(new BeltItem(0, 0f));   // 뒤쪽(작은 position)
        segment.Items.Add(new BeltItem(0, 4.9f)); // 앞쪽(끝에 거의 도달)

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        // 다음 세그먼트도 목표 기계도 없는 막다른 벨트 -> 큰 델타를 줘도 끝에서 대기해야 함
        system.Tick(1f, segments, processors, null);
        Assert.AreEqual(5f, segment.Items[1].Position, 0.001f, "프론트 아이템은 막다른 끝에서 대기");
        Assert.AreEqual(5f - segment.ItemSpacing, segment.Items[0].Position, 0.001f, "뒤 아이템은 프론트를 추월하지 못하고 압축되어 대기");
    }

    [Test]
    public void Tick_ItemCarriesOverflowAcrossSegmentBoundary_NoStutter()
    {
        // 연결부위 멈칫 버그 회귀 테스트: 프론트 아이템이 한 틱에 경계를 넘어야 할 때,
        // 다음 세그먼트에서 position 0이 아니라 실제 overflow만큼 이어서 시작해야 한다.
        var segment0 = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, NextSegmentId = 1 };
        var segment1 = new BeltSegment { Id = 1, Length = 1f };
        segment0.Items.Add(new BeltItem(0, 0.95f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { segment0, segment1 };
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.1f, segments, processors, null); // desired = 0.95 + 2*0.1 = 1.15 -> overflow 0.15

        Assert.AreEqual(0, segment0.Items.Count, "경계를 넘은 아이템은 이번 세그먼트에서 사라져야 함");
        Assert.AreEqual(1, segment1.Items.Count);
        Assert.AreEqual(0.15f, segment1.Items[0].Position, 0.001f, "position 0이 아니라 overflow만큼 이어서 시작해야 함");
    }

    [Test]
    public void Tick_ProcessesDownstreamFirst_EvenWhenArrayOrderIsUpstreamFirst()
    {
        // 회귀 테스트: 기존 벨트 "앞쪽"에 나중에 이어붙이면, 새 세그먼트가 리스트엔 나중에
        // 추가되지만 실제로는 상류다 (배열 인덱스가 소스->목적지 순서와 반대). 예전엔 이 경우
        // 경계에서 낡은 상태로 판단해 아이템이 멈춰있는 것처럼 보이는 버그가 있었다.
        var downstream = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 5f }; // 배열상 먼저(index 0)지만 실제로는 하류
        var upstream = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 5f, NextSegmentId = 0 }; // 배열상 나중(index 1)이지만 실제로는 상류
        upstream.Items.Add(new BeltItem(0, 0.9f));

        var system = new BeltSystem();
        var segments = new List<BeltSegment> { downstream, upstream }; // 일부러 소스->목적지 반대 순서로 전달
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        for (int i = 0; i < 20; i++)
        {
            system.Tick(0.05f, segments, processors, null);
        }

        Assert.AreEqual(0, upstream.Items.Count, "상류 세그먼트에서 아이템이 하류로 넘어갔어야 함");
        Assert.AreEqual(1, downstream.Items.Count);
        Assert.Greater(downstream.Items[0].Position, 0f, "하류로 넘어간 뒤에도 계속 전진해야 함 (제자리에 멈춰있으면 안 됨)");
    }

    [Test]
    public void Tick_LongRun_NoItemLossOrDuplication_AcrossProcessorBeltProcessorChain()
    {
        // 채굴기는 벨트 소스가 될 수 없으므로(원격 전송, MinerSystem 참고), 여기서는 일반
        // Processor를 소스로 써서 벨트 자체의 장시간 무유실/무중복 동작만 검증한다.
        var db = BuildMinimalDatabase(out int resourceId);

        var source = new ProcessorInstance(db.ResourceCount);
        source.OutputBuffer[resourceId] = 20;
        var target = new ProcessorInstance(db.ResourceCount);

        var segment0 = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, NextSegmentId = 1 };
        var segment1 = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 2f, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment0, segment1 };
        var processors = new List<ProcessorInstance> { source, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 500; i++)
        {
            system.Tick(0.05f, segments, processors, null);
        }

        int onBelt = segment0.Items.Count + segment1.Items.Count;
        int total = source.OutputBuffer[resourceId] + onBelt + target.InputBuffer[resourceId];

        Assert.AreEqual(20, total, "산출 대기+벨트 위+기계 투입분의 합은 항상 원래 산출량과 같아야 함 (유실/중복 없음)");
        Assert.AreEqual(0, source.OutputBuffer[resourceId], "충분히 오래 돌면 모두 벨트로 실려 나가야 함");
        Assert.AreEqual(20, target.InputBuffer[resourceId]);
    }

    [Test]
    public void CoreSource_DeadEndBelt_NeverDispenses()
    {
        // 사용자가 보고한 버그: 코어에서 아무 목적지도 없는(막다른) 벨트를 뽑아두면 그것만으로
        // 재고가 흘러나오면 안 된다 — 받으려는 기계가 없으면 코어가 내줄 이유가 없다.
        var db = BuildMinimalDatabase(out int oreId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 10;

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0 }; // NextSegmentId/TargetProcessorId 둘 다 없음(막다른 벨트)
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, segment.Items.Count, "막다른 벨트에는 코어가 아무것도 흘려보내면 안 됨");
        Assert.AreEqual(10, core.InputBuffer[oreId], "재고도 그대로 남아있어야 함");
    }

    [Test]
    public void CoreSource_TargetWithoutRecipe_NeverDispenses()
    {
        // 목적지 기계는 있지만 아직 레시피를 지정 안 했으면("정보를 전달받기 전") 코어가
        // 뭘 원하는지 모르니 아무것도 내주면 안 된다.
        var db = BuildMinimalDatabase(out int oreId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 10;
        var target = new ProcessorInstance(db.ResourceCount); // RecipeId 기본값 -1(미지정)

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(0, segment.Items.Count, "레시피 미지정 기계로는 코어가 아무것도 흘려보내면 안 됨");
        Assert.AreEqual(10, core.InputBuffer[oreId]);
    }

    [Test]
    public void CoreSource_TargetWithRecipe_DispensesOnlyTheNeededResource()
    {
        // 코어에 두 종류가 쌓여 있어도, 목적지 레시피가 필요로 하는 자원만 내줘야 한다
        // ("레시피를 지정하면 그 정보를 전달받아 필요한 것만 준다"는 원래 설계).
        var db = BuildDatabaseWithRecipe(out int oreId, out int scrapId, out int recipeId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[oreId] = 5;
        core.InputBuffer[scrapId] = 5; // 레시피와 무관한 자원도 같이 쌓여있음
        var target = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId }; // TestOre만 필요

        var segment = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceProcessorId = 0, TargetProcessorId = 1 };
        var segments = new List<BeltSegment> { segment };
        var processors = new List<ProcessorInstance> { core, target };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 50; i++) system.Tick(0.1f, segments, processors, db);

        Assert.AreEqual(5, core.InputBuffer[scrapId], "레시피가 필요로 하지 않는 자원은 그대로 남아있어야 함");
        Assert.Less(core.InputBuffer[oreId], 5, "레시피가 필요로 하는 자원은 실제로 빠져나가야 함");
    }

    private static GameDatabase BuildDatabaseWithRecipe(out int oreId, out int scrapId, out int recipeId)
    {
        var ore = ScriptableObject.CreateInstance<ResourceDef>();
        ore.resourceId = "TestOre";
        var scrap = ScriptableObject.CreateInstance<ResourceDef>();
        scrap.resourceId = "TestScrap";

        var recipe = ScriptableObject.CreateInstance<RecipeDef>();
        recipe.recipeId = "TestRecipe";
        recipe.inputs = new[] { new RecipeIngredient { resource = ore, amount = 1 } };
        recipe.outputs = System.Array.Empty<RecipeIngredient>();
        recipe.processSeconds = 1f;
        recipe.requiredCategory = MachineCategory.Smelter;

        var db = GameDatabase.Build(new[] { ore, scrap }, new[] { recipe }, System.Array.Empty<MachineDef>());
        oreId = db.GetResourceId("TestOre");
        scrapId = db.GetResourceId("TestScrap");
        recipeId = db.GetRecipeId("TestRecipe");

        Object.DestroyImmediate(ore);
        Object.DestroyImmediate(scrap);
        Object.DestroyImmediate(recipe);
        return db;
    }

    private static GameDatabase BuildMinimalDatabase(out int resourceId)
    {
        var ore = ScriptableObject.CreateInstance<ResourceDef>();
        ore.resourceId = "TestOre";

        var db = GameDatabase.Build(new[] { ore }, System.Array.Empty<RecipeDef>(), System.Array.Empty<MachineDef>());
        resourceId = db.GetResourceId("TestOre");

        Object.DestroyImmediate(ore);
        return db;
    }
}
