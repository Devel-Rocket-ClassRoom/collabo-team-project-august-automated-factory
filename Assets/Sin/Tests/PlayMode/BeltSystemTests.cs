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
        var miners = new List<MinerInstance>();
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.5f, segments, miners, processors);
        Assert.AreEqual(0.5f, segment.Items[0].Position, 0.001f);

        system.Tick(0.5f, segments, miners, processors);
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
        var miners = new List<MinerInstance>();
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        // 다음 세그먼트도 목표 기계도 없는 막다른 벨트 -> 큰 델타를 줘도 끝에서 대기해야 함
        system.Tick(1f, segments, miners, processors);

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
        var miners = new List<MinerInstance>();
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        system.Tick(0.1f, segments, miners, processors); // desired = 0.95 + 2*0.1 = 1.15 -> overflow 0.15

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
        var miners = new List<MinerInstance>();
        var processors = new List<ProcessorInstance>();
        system.Configure(segments);

        for (int i = 0; i < 20; i++)
        {
            system.Tick(0.05f, segments, miners, processors);
        }

        Assert.AreEqual(0, upstream.Items.Count, "상류 세그먼트에서 아이템이 하류로 넘어갔어야 함");
        Assert.AreEqual(1, downstream.Items.Count);
        Assert.Greater(downstream.Items[0].Position, 0f, "하류로 넘어간 뒤에도 계속 전진해야 함 (제자리에 멈춰있으면 안 됨)");
    }

    [Test]
    public void Tick_LongRun_NoItemLossOrDuplication_AcrossMinerBeltProcessorChain()
    {
        var db = BuildMinimalDatabase(out int resourceId);

        var miner = new MinerInstance { OutputResourceId = resourceId, BufferedOutput = 20 };
        var processor = new ProcessorInstance(db.ResourceCount);

        var segment0 = new BeltSegment { Id = 0, Length = 1f, SpeedUnitsPerSecond = 2f, SourceMinerId = 0, NextSegmentId = 1 };
        var segment1 = new BeltSegment { Id = 1, Length = 1f, SpeedUnitsPerSecond = 2f, TargetProcessorId = 0 };
        var segments = new List<BeltSegment> { segment0, segment1 };
        var miners = new List<MinerInstance> { miner };
        var processors = new List<ProcessorInstance> { processor };

        var system = new BeltSystem();
        system.Configure(segments);

        for (int i = 0; i < 500; i++)
        {
            system.Tick(0.05f, segments, miners, processors);
        }

        int onBelt = segment0.Items.Count + segment1.Items.Count;
        int total = miner.BufferedOutput + onBelt + processor.InputBuffer[resourceId];

        Assert.AreEqual(20, total, "미채굴+벨트 위+기계 투입분의 합은 항상 원래 채굴량과 같아야 함 (유실/중복 없음)");
        Assert.AreEqual(0, miner.BufferedOutput, "충분히 오래 돌면 모두 벨트로 실려 나가야 함");
        Assert.AreEqual(20, processor.InputBuffer[resourceId]);
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
