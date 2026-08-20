namespace Factory.Simulation
{
    // 제련로/조립기 등 "레시피를 소비해서 산출한다" 유형 기계 한 대의 런타임 상태.
    // 어떤 레시피인지는 RecipeId(데이터)로만 결정되고, 이 클래스와 ProcessorSystem은
    // 레시피별 분기를 두지 않는다 — 새 레시피 추가가 코드 무변경으로 동작하는 근거.
    public sealed class ProcessorInstance
    {
        public int MachineId;
        public int RecipeId = -1;
        public float SpeedMultiplier = 1f;

        public bool IsProcessing;
        public float Progress;

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
            if (InputBuffer[resourceId] + amount > SimulationConstants.ResourceBufferCapacity) return false;
            InputBuffer[resourceId] += amount;
            return true;
        }
    }
}
