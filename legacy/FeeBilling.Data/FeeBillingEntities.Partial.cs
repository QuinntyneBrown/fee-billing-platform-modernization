namespace FeeBilling.Data
{
    public partial class FeeBillingEntities
    {
        // Added for the month-end export tool, which builds its connection string at runtime.
        // Expects an EF connection string (metadata=res://*/FeeBilling.csdl|...) or "name=...".
        public FeeBillingEntities(string nameOrConnectionString)
            : base(nameOrConnectionString)
        {
        }
    }
}
