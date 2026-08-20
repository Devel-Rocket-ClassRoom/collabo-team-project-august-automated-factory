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
        var db = BuildMinimalDatabase(out int resourceId);
        var world = new SimulationWorld(db);

        // MineIntervalSeconds를 매우 크게 둬서 테스트 동안 추가로 자동 채굴되지 않게 하고,
        // 미리 채워둔 BufferedOutput 5개만으로 벨트 전달 동작을 검증한다.
        int minerIndex = world.AddMiner(new MinerInstance
        {
            OutputResourceId = resourceId,
            BufferedOutput = 5,
            MineIntervalSeconds = float.MaxValue,
        });
        int processorIndex = world.AddProcessor(new ProcessorInstance(db.ResourceCount));

        int segment0Id = world.Segments.Count;
        world.AddBeltSegment(new BeltSegment { Id = segment0Id, Length = 1f, SpeedUnitsPerSecond = 5f, SourceMinerId = minerIndex });

        int segment1Id = world.Segments.Count;
        world.AddBeltSegment(new BeltSegment { Id = segment1Id, Length = 1f, SpeedUnitsPerSecond = 5f, TargetProcessorId = processorIndex });
        world.Segments[0].NextSegmentId = segment1Id;

        for (int i = 0; i < 200; i++) world.Tick(0.05f);

        Assert.AreEqual(0, world.Miners[minerIndex].BufferedOutput);
        Assert.AreEqual(5, world.Processors[processorIndex].InputBuffer[resourceId]);
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
