namespace Factory.Simulation
{
    public struct BeltItem
    {
        public int ResourceId;
        public float Position; // 0 = 세그먼트 시작, Length = 세그먼트 끝

        public BeltItem(int resourceId, float position)
        {
            ResourceId = resourceId;
            Position = position;
        }
    }
}
