namespace Factory.Data
{
    public readonly struct ResourceAmount
    {
        public readonly int ResourceId;
        public readonly int Amount;

        public ResourceAmount(int resourceId, int amount)
        {
            ResourceId = resourceId;
            Amount = amount;
        }
    }
}
