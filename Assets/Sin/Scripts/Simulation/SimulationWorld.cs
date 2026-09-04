using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 한 판의 시뮬레이션 상태(기계 인스턴스, 벨트) + 매 틱 실행 순서를 들고 있는 컨테이너.
    // MonoBehaviour가 아닌 POCO라 씬 없이 유닛 테스트로 직접 구성/검증할 수 있다.
    //
    // 게임 시작 시 한 번에 채워지는 게 아니라 플레이 중 하나씩 놓이므로(터치 건설),
    // 배열이 아니라 List로 들고 있고 Add* 메서드로 점진적으로 늘어난다.
    public sealed class SimulationWorld
    {
        public readonly GameDatabase Database;
        public readonly WorldGrid Grid = new WorldGrid();

        public List<MinerInstance> Miners { get; } = new List<MinerInstance>();
        public List<ProcessorInstance> Processors { get; } = new List<ProcessorInstance>();
        public List<BeltSegment> Segments { get; } = new List<BeltSegment>();

        // 채굴기가 원격 전송으로 곧장 넣어줄 코어의 인덱스(CoreSpawner가 설정). 아직 코어가
        // 없으면 -1이고, 그동안 채굴한 산출물은 MinerInstance.BufferedOutput에 대기한다.
        public int CoreProcessorIndex = -1;

        private readonly MinerSystem minerSystem = new MinerSystem();
        private readonly ProcessorSystem processorSystem = new ProcessorSystem();
        private readonly BeltSystem beltSystem = new BeltSystem();
        private readonly RoutingSystem routingSystem = new RoutingSystem();

        public SimulationWorld(GameDatabase database)
        {
            Database = database;
        }

        public int AddMiner(MinerInstance miner)
        {
            Miners.Add(miner);
            return Miners.Count - 1;
        }

        public int AddProcessor(ProcessorInstance processor)
        {
            Processors.Add(processor);
            return Processors.Count - 1;
        }

        // segments는 체인별로 소스→목적지 순서로 추가해야 한다 (BeltSystem 참고).
        public int AddBeltSegment(BeltSegment segment)
        {
            Segments.Add(segment);
            beltSystem.Configure(Segments);
            return Segments.Count - 1;
        }

        // 철거 전용 — 리스트 중간 아무 인덱스나 지울 수 있어야 한다(플레이어가 아무 기계나
        // 골라 지우므로, 항상 최근 것만 지우던 것과 다름). 그런데 InstanceIndex가 "리스트
        // 인덱스"와 같다는 전제가 WorldGrid/BeltSystem 곳곳에 깔려 있어서, 그냥 List.RemoveAt으로
        // 지우면 그 뒤 모든 occupant의 인덱스가 한 칸씩 밀려서 완전히 틀어진다. 그래서 실제로
        // 지우지 않고 그 자리를 null로 비워두는 "톰스톤" 방식을 쓴다 — 인덱스는 절대 안 바뀌고,
        // Tick 루프들만 null 슬롯을 건너뛰면 된다(MinerSystem/ProcessorSystem/BeltSystem 참고).
        //
        // 지우는 대상이 그 순간 들고 있던 자원(벨트 위 아이템, 기계 버퍼, 채굴기가 아직 코어로
        // 못 보낸 산출물)은 그냥 사라지면 안 되고 코어로 환불한다 — 안 그러면 철거를 타이밍
        // 나쁘게 쓸 때마다 자원이 조용히 증발한다.
        public void RemoveMiner(int index)
        {
            if (index < 0 || index >= Miners.Count || Miners[index] == null) return;

            RefundToCore(Miners[index].OutputResourceId, Miners[index].BufferedOutput);
            Miners[index] = null;
        }

        public void RemoveProcessor(int index)
        {
            if (index < 0 || index >= Processors.Count || Processors[index] == null) return;

            var processor = Processors[index];
            for (int r = 0; r < processor.InputBuffer.Length; r++) RefundToCore(r, processor.InputBuffer[r]);
            for (int r = 0; r < processor.OutputBuffer.Length; r++) RefundToCore(r, processor.OutputBuffer[r]);
            Processors[index] = null;

            // 이 프로세서를 참조하던 벨트 세그먼트들의 연결을 끊는다 — 안 그러면 다음 틱에
            // 지금 null이 된 자리를 그대로 인덱싱해서 예외가 난다.
            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (segment == null) continue;
                if (segment.SourceProcessorId == index) segment.SourceProcessorId = null;
                if (segment.TargetProcessorId == index) segment.TargetProcessorId = null;
            }
        }

        public void RemoveSegment(int id)
        {
            if (id < 0 || id >= Segments.Count || Segments[id] == null) return;

            var items = Segments[id].Items;
            for (int j = 0; j < items.Count; j++) RefundToCore(items[j].ResourceId, 1);
            Segments[id] = null;

            // 이 세그먼트로 흘러들던 상류 세그먼트의 연결을 끊는다(RemoveProcessor가
            // SourceProcessorId/TargetProcessorId를 끊어주는 것과 같은 취지). Tick 루프만 보면
            // segmentsById.TryGetValue가 못 찾아서 "다음 없음"으로 안전하게 넘어가지만, 건설
            // 도구는 NextSegmentId.HasValue를 직접 봐서 "이미 다른 곳으로 흐르는 벨트"로 판단해
            // 재연결을 거부한다(BeltDragTool.Commit / TryDirectLink) — 그래서 철거한 자리에 새
            // 벨트를 다시 못 잇는 버그가 있었다. dangling 참조를 실제로 지워야 한다.
            for (int i = 0; i < Segments.Count; i++)
            {
                if (Segments[i] != null && Segments[i].NextSegmentId == id) Segments[i].NextSegmentId = null;
            }

            beltSystem.Configure(Segments); // segmentsById 캐시에서 지워진 id를 빼서 다시 맞춘다.
        }

        // 코어 용량(9999)이 넉넉해서 사실상 항상 다 받아준다 — 실패해도 자원을 잃는 것보단
        // 넘치는 만큼만 잘리는 게 낫다(Math.Min으로 클램프, TryAcceptInput의 전부-거절 방식 아님).
        // 코어가 아직 없거나(이론상으로만 가능) 이미 null이면 돌려줄 곳이 없으니 그냥 버린다.
        private void RefundToCore(int resourceId, int amount)
        {
            if (amount <= 0) return;
            if (CoreProcessorIndex < 0 || CoreProcessorIndex >= Processors.Count) return;

            var core = Processors[CoreProcessorIndex];
            if (core == null) return;
            core.InputBuffer[resourceId] = System.Math.Min(core.InputBuffer[resourceId] + amount, core.Capacity);
        }

        public void Tick(float deltaSeconds)
        {
            minerSystem.Tick(deltaSeconds, Miners, Processors, CoreProcessorIndex);
            processorSystem.Tick(deltaSeconds, Database, Processors);
            beltSystem.Tick(deltaSeconds, Segments, Processors, Database);
            // 벨트가 이번 틱에 라우팅 노드 InputBuffer로 배달한 것을, 곧바로 출력 벨트에 분배/병합한다.
            routingSystem.Tick(Processors, Segments);
        }
    }
}
