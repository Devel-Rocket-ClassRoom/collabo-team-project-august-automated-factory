namespace Factory.Simulation
{
    public static class SimulationConstants
    {
        public const int ResourceBufferCapacity = 50;
        public const float DefaultBeltSpeed = 1.5f;
        public const float DefaultItemSpacing = 0.35f;

        // 벨트가 감당할 수 있는 최대 속도(ItemSpacing / BeltSpeed ≈ 0.23초당 1개)에 맞춰뒀다.
        // 이보다 채굴 주기가 느리면(예전 2초) 벨트 연결 전 쌓인 백로그가 초반엔 벨트 속도로
        // 빠르게 빠져나오다가, 백로그가 바닥나면 훨씬 느린 채굴 속도로 뚝 떨어지는 게 눈에
        // 띄게 된다. 채굴 속도를 벨트 처리량에 맞춰야 "일정한 속도"로 보인다.
        public const float DefaultMineIntervalSeconds = 0.25f;

        // 화면 프레임률(보통 60)에 가깝게 맞춰서, 틱 사이 위치가 그대로인 구간이 눈에 띄지
        // 않게 한다. 시뮬레이션 규모가 커져서 60Hz가 부담되면, 틱레이트를 낮추는 대신
        // BeltItemRenderer에서 프레임마다 이전/현재 틱 위치를 보간하는 방식으로 바꿔야 한다
        // (지금은 렌더러가 틱 결과값을 그대로 그리기만 해서, 틱레이트가 곧 체감 부드러움).
        public const float DefaultTickRate = 60f;
    }
}
