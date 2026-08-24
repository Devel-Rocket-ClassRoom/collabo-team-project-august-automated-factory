namespace Factory.Data
{
    // 기획서 10종 기계의 대분류. 이번 단계에서는 Miner/Smelter/Assembler만 실제로 쓰이고,
    // 나머지는 이후 단계(전력망, 저장고, 연구소)에서 재도입한다.
    public enum MachineCategory
    {
        Miner,
        Smelter,
        Assembler,
        Storage,
        Generator,
        ResearchLab,
    }
}
