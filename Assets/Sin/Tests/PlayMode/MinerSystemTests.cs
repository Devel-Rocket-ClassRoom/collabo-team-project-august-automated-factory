using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

// 채굴기는 입출력 포트가 없다 — 캔 자원은 벨트 없이 코어(UniversalPorts 켜진 Processor)로
// 곧바로 "원격 전송"된다. MinerSystem이 그 배송 자체를 담당한다.
public class MinerSystemTests
{
    [Test]
    public void MinedOutput_DeliversDirectlyToCore_NoBeltNeeded()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        var processors = new List<ProcessorInstance> { core };
        var miner = new MinerInstance { OutputResourceId = resourceId, MineIntervalSeconds = 1f };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        for (int i = 0; i < 10; i++) system.Tick(1f, miners, processors, coreProcessorIndex: 0);

        Assert.AreEqual(0, miner.BufferedOutput, "채굴한 만큼 매번 코어로 바로 빠져나가야 함");
        Assert.AreEqual(10, core.InputBuffer[resourceId]);
    }

    [Test]
    public void CoreFull_MinerKeepsBuffering_ThenFlushesOnceSpaceFrees()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var core = new ProcessorInstance(db.ResourceCount) { RecipeId = -1, UniversalPorts = true };
        core.InputBuffer[resourceId] = SimulationConstants.ResourceBufferCapacity; // 이미 가득 참
        var processors = new List<ProcessorInstance> { core };
        var miner = new MinerInstance { OutputResourceId = resourceId, BufferedOutput = 3, MineIntervalSeconds = float.MaxValue };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        system.Tick(0.1f, miners, processors, coreProcessorIndex: 0);

        Assert.AreEqual(3, miner.BufferedOutput, "코어가 가득 차 있으면 잃지 않고 채굴기 쪽에 대기해야 함");

        core.InputBuffer[resourceId] -= 2; // 공간이 생기면
        system.Tick(0.1f, miners, processors, coreProcessorIndex: 0);

        Assert.AreEqual(1, miner.BufferedOutput, "생긴 공간만큼만 흘러들어가야 함");
        Assert.AreEqual(SimulationConstants.ResourceBufferCapacity, core.InputBuffer[resourceId]);
    }

    [Test]
    public void NoCoreRegistered_MinerJustBuffers_NoExceptionNoLoss()
    {
        var db = BuildMinimalDatabase(out int resourceId);
        var processors = new List<ProcessorInstance>();
        var miner = new MinerInstance { OutputResourceId = resourceId, MineIntervalSeconds = 1f };
        var miners = new List<MinerInstance> { miner };

        var system = new MinerSystem();
        Assert.DoesNotThrow(() => system.Tick(3f, miners, processors, coreProcessorIndex: -1));

        Assert.AreEqual(3, miner.BufferedOutput, "코어가 아직 없으면 잃지 않고 채굴기에 쌓여 있어야 함");
    }

    private static GameDatabase BuildMinimalDatabase(out int resourceId)
    {
        var ore = new ItemData { itemID = "TestOre" };

        var db = GameDatabase.Build(new[] { ore }, System.Array.Empty<MachineData>(), System.Array.Empty<RecipeData>());
        resourceId = db.GetResourceId("TestOre");

        return db;
    }
}
