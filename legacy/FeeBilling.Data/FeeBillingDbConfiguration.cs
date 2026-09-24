using System.Data.Entity;
using System.Data.Entity.SqlServer;

namespace FeeBilling.Data
{
    /// <summary>
    /// Code-based EF6 configuration, discovered automatically because it lives in the same
    /// assembly as the contexts. Means console tools only need a connection string, not the
    /// whole &lt;entityFramework&gt; config section.
    /// </summary>
    public class FeeBillingDbConfiguration : DbConfiguration
    {
        public FeeBillingDbConfiguration()
        {
            SetProviderServices(SqlProviderServices.ProviderInvariantName, SqlProviderServices.Instance);
        }
    }
}
