using UnityEngine;

namespace Factory.Simulation
{
    // 레시피 없는 벨트 라우팅 노드. None = 일반 기계/코어. Splitter는 입력 1개를 출력 여러
    // 벨트에 라운드로빈 분배, Merger는 입력 여러 벨트를 출력 1개로 병합한다(RoutingSystem 참고).
    public enum RoutingRole
    {
        None,
        Splitter,
        Merger,
    }

    // 제련로/성형기/합성기 등 "레시피를 소비해서 산출한다" 유형 기계 한 대의 런타임 상태.
    // 어떤 레시피인지는 RecipeId(데이터)로만 결정되고, 이 클래스와 ProcessorSystem은
    // 레시피별 분기를 두지 않는다 — 새 레시피 추가가 코드 무변경으로 동작하는 근거.
    //
    // 코어도 이 타입을 그대로 재사용한다 (RecipeId=-1이라 ProcessorSystem이 건드리지
    // 않고, 그냥 아무거나 받아서 쌓아두기만 하는 저장소가 됨) — UniversalPorts만 true로
    // 켜서 4면 다 입출력 가능하게 구분한다.
    public sealed class ProcessorInstance
    {
        public int MachineId;
        public int RecipeId = -1;
        public float SpeedMultiplier = 1f;

        // 전력 유무는 여기 하나로만 판정한다. PowerGridSystem은 전력 없으면 SpeedMultiplier를
        // 0으로 깎아서(시뮬레이션 코드는 안 건드리는 설계) 끈다 — 그런데 "0인지"를 여기저기서
        // 직접 float 비교로 매번 새로 판단하면, 어딘가 하나 빠뜨렸을 때(예: 사이클 시작 체크에
        // 안 넣음, 연기 이펙트에 안 넣음) 조용히 새는 구멍이 생긴다(실제로 둘 다 겪음). 전력에
        // 반응해야 하는 코드는 전부 SpeedMultiplier를 직접 보지 말고 이 프로퍼티만 봐야 한다.
        //
        // 켜져 있을 때(IsPowered=true): 새 사이클 시작(재료 소비) 가능, Progress 진행, 연기 등
        //   "작동 중" 시각효과 재생.
        // 꺼져 있을 때(IsPowered=false): 새 사이클을 시작하지 않음(재료 안 먹음), 이미 진행
        //   중이던 사이클은 Progress가 그 자리에서 멈춘 채 대기(전력 들어오면 이어서 진행 —
        //   재료를 이미 냈으니 취소하지 않고 자연스럽게 이어가는 게 맞음), 시각효과 정지.
        public bool IsPowered => SpeedMultiplier > 0f;

        // 벨트 라우팅 노드(분류기/합류기)면 None이 아니다. RoutingSystem이 이 값으로 분기하고,
        // ProcessorSystem은 RecipeId<0라 어차피 건드리지 않는다. RoutingCursor는 라운드로빈
        // 위치(분류기 = 다음 출력 벨트 인덱스, 합류기 = 마지막으로 내보낸 자원 id).
        public RoutingRole RoutingRole = RoutingRole.None;
        public int RoutingCursor;

        // 현재 진행 중인 사이클이 실제로 재료를 소비한 레시피. RecipeId는 사용자가 언제든
        // (처리 도중에도) 바꿀 수 있지만, 이미 시작된 사이클은 끝까지 이 값 기준으로
        // 완료되어야 한다 — 안 그러면 옛 레시피 재료로 새 레시피 산출물을 공짜로 만들어내게 된다.
        public int ActiveRecipeId = -1;

        // 입력 포트 = 이 기계가 놓인 셀 - Facing, 출력 포트 = 놓인 셀 + Facing.
        public Vector2Int Facing = new Vector2Int(1, 0);
        // true면(코어) 고정 포트 대신 4면 전부 입출력 가능.
        public bool UniversalPorts;
        public bool IsGeneratorFuelPort;
        public int OwnerPowerNodeId = -1;
        public int CoalResourceId = -1;
        public int BatteryResourceId = -1;
        public int SelectedFuelResourceId = -1;

        // footprint가 1칸보다 큰 기계(예: 2x2 합성기)의 포트 계산 기준. 어느 footprint 칸을
        // 밟아서 연결하든 항상 이 앵커 기준으로 포트 위치를 계산한다(GridUtility.GetPortCells).
        public Vector2Int Anchor;
        public Vector2Int Footprint = Vector2Int.one;

        public bool IsProcessing;
        public float Progress;

        // 인스턴스별 버퍼 용량(기본은 일반 기계 값, 코어는 CoreSpawner에서 훨씬 크게 설정).
        public int Capacity = SimulationConstants.ResourceBufferCapacity;

        // 자원 id로 인덱싱되는 고정 크기 버퍼. GameDatabase.ResourceCount에 맞춰 1회 할당.
        public int[] InputBuffer;
        public int[] OutputBuffer;

        public ProcessorInstance(int resourceCount)
        {
            InputBuffer = new int[resourceCount];
            OutputBuffer = new int[resourceCount];
        }

        public bool TryAcceptInput(int resourceId, int amount)
        {
            if (resourceId < 0 || resourceId >= InputBuffer.Length || amount <= 0) return false;
            if (IsGeneratorFuelPort)
            {
                if (resourceId != SelectedFuelResourceId) return false;
                int stored = 0;
                for (int i = 0; i < InputBuffer.Length; i++) stored += InputBuffer[i];
                if (stored + amount > Capacity) return false;
            }
            if (InputBuffer[resourceId] + amount > Capacity) return false;
            InputBuffer[resourceId] += amount;
            return true;
        }
    }
}
