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

        private readonly MinerSystem minerSystem = new MinerSystem();
        private readonly ProcessorSystem processorSystem = new ProcessorSystem();
        private readonly BeltSystem beltSystem = new BeltSystem();

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

        public void Tick(float deltaSeconds)
        {
            minerSystem.Tick(deltaSeconds, Miners);
            processorSystem.Tick(deltaSeconds, Database, Processors);
            beltSystem.Tick(deltaSeconds, Segments, Miners, Processors);
        }
    }
}
