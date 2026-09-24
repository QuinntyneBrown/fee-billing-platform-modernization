using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace FeeBilling.Data
{
    /// <summary>
    /// Invoice exports. ADO.NET because EF was "too slow" for the export in 2013.
    /// Uses its own connection string (FeeBillingAdo) - keep it in sync with FeeBillingEntities.
    /// </summary>
    public static class InvoiceRepository
    {
        public static DataSet GetInvoiceDataSet(int runId)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["FeeBillingAdo"].ConnectionString;

            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand("dbo.usp_GetInvoicesForRun", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@RunId", runId);

                var ds = new DataSet();
                using (var adapter = new SqlDataAdapter(command))
                {
                    adapter.Fill(ds);   // whole run in memory; 40k rows for the big firms
                }
                return ds;
            }
        }

        public static DataTable GetReconciliation(int runId)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["FeeBillingAdo"].ConnectionString;

            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand("dbo.usp_ReconcileFeeDebits", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@RunId", runId);

                var table = new DataTable("Reconciliation");
                using (var adapter = new SqlDataAdapter(command))
                {
                    adapter.Fill(table);
                }
                return table;
            }
        }
    }
}
