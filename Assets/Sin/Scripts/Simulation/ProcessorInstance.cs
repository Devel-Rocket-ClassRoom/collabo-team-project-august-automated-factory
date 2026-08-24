using UnityEngine;

namespace Factory.Simulation
{
    // 제련로/조립기 등 "레시피를 소비해서 산출한다" 유형 기계 한 대의 런타임 상태.
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

        // 입력 포트 = 이 기계가 놓인 셀 - Facing, 출력 포트 = 놓인 셀 + Facing.
        public Vector2Int Facing = new Vector2Int(1, 0);
        // true면(코어) 고정 포트 대신 4면 전부 입출력 가능.
        public bool UniversalPorts;

        // footprint가 1칸보다 큰 기계(예: 2x2 조립기)의 포트 계산 기준. 어느 footprint 칸을
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
            if (InputBuffer[resourceId] + amount > Capacity) return false;
            InputBuffer[resourceId] += amount;
            return true;
        }
    }
}
