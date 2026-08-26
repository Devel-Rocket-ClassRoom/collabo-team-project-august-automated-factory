namespace Factory.Simulation
{
    // 채굴기 한 대의 런타임 상태. GameObject가 아니라 SimulationWorld가 배열로 들고 순회하는 순수 데이터.
    // 입출력 포트가 없다 — 캔 자원은 벨트 없이 곧바로 코어로 원격 전송된다(MinerSystem 참고).
    public sealed class MinerInstance
    {
        public int MachineId;
        public int OutputResourceId;
        public float SpeedMultiplier = 1f;
        public float MineIntervalSeconds = SimulationConstants.DefaultMineIntervalSeconds;
        // 아래 밟고 있는 광물 노드(OreDepositDef)가 정한 사이클당 산출량.
        public int YieldPerCycle = 1;

        public float Progress;
        public int BufferedOutput;
    }
}
