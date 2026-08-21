using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

public class SimulationWorldTests
{
    [Test]
    public void AddMethods_ReturnSequentialIndices()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var world = new SimulationWorld(db);

        int minerIndex0 = world.AddMiner(new MinerInstance { OutputResourceId = resourceId });
        int minerIndex1 = world.AddMiner(new MinerInstance { OutputResourceId = resourceId });
        int processorIndex0 = world.AddProcessor(new ProcessorInstance(db.ResourceCount));
        int segmentIndex0 = world.AddBeltSegment(new BeltSegment { Id = 0, Length = 1f });

        Assert.AreEqual(0, minerIndex0);
        Assert.AreEqual(1, minerIndex1);
        Assert.AreEqual(0, processorIndex0);
        Assert.AreEqual(0, segmentIndex0);
        Assert.AreEqual(2, world.Miners.Count);
        Assert.AreEqual(1, world.Processors.Count);
        Assert.AreEqual(1, world.Segments.Count);
    }

    [Test]
    public void IncrementallyAddedSegments_StillTickCorrectly()
    {
        // 벨트 드래그 도구가 하는 것처럼 미리 다 만들어두지 않고 하나씩 늘려도
        // BeltSystem이 정상 동작해야 한다 (id->세그먼트 딕셔너리가 Add마다 갱신되는지 확인).
        // 채굴기는 벨트 소스가 될 수 없으므로(원격 전송), 소스 역할은 일반 Processor로 대신한다.
        var db = BuildMinimalDatabase(out int resourceId);
        var world = new SimulationWorld(db);

        var sourceProcessor = new ProcessorInstance(db.ResourceCount);
        sourceProcessor.OutputBuffer[resourceId] = 5;
        int sourceIndex = world.AddProcessor(sourceProcessor);
        int targetIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount));

        int segment0Id = world.Segments.Count;
        world.AddBeltSegment(new BeltSegment { Id = segment0Id, Length = 1f, SpeedUnitsPerSecond = 5f, SourceProcessorId = sourceIndex });

        int segment1Id = world.Segments.Count;
        world.AddBeltSegment(new BeltSegment { Id = segment1Id, Length = 1f, SpeedUnitsPerSecond = 5f, TargetProcessorId = targetIndex });
        world.Segments[0].NextSegmentId = segment1Id;

        for (int i = 0; i < 200; i++) world.Tick(0.05f);

        Assert.AreEqual(0, world.Processors[sourceIndex].OutputBuffer[resourceId]);
        Assert.AreEqual(5, world.Processors[targetIndex].InputBuffer[resourceId]);
    }

    [Test]
    public void MinerOutput_DeliversDirectlyToCore_WithoutAnyBelt()
    {
        // 채굴기는 입출력 포트가 없다 — 벨트를 하나도 안 놓아도, 코어로 지정된 Processor에
        // 캔 만큼 곧바로("원격 전송") 쌓여야 한다.
        var db = BuildMinimalDatabase(out int resourceId);
        var world = new SimulationWorld(db);

        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        int coreIndex = world.AddProcessor(core);
        world.CoreProcessorIndex = coreIndex;

        world.AddMiner(new MinerInstance { OutputResourceId = resourceId, BufferedOutput = 5, MineIntervalSeconds = float.MaxValue });

        world.Tick(0.05f);

        Assert.AreEqual(0, world.Segments.Count, "벨트를 하나도 안 놓았어야 함");
        Assert.AreEqual(0, world.Miners[0].BufferedOutput, "채굴기 버퍼는 즉시 코어로 비워져야 함");
        Assert.AreEqual(5, core.InputBuffer[resourceId], "코어 재고로 곧바로 들어가 있어야 함");
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
