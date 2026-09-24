using System;
using System.Data;
using System.Linq;
using System.Text;

namespace FeeBilling.Web.Helpers
{
    public static class CsvExporter
    {
        public static string ToCsv(DataTable table)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
            foreach (DataRow row in table.Rows)
            {
                sb.AppendLine(string.Join(",", row.ItemArray.Select(FormatValue)));
            }
            return sb.ToString();
        }

        public static string FormatValue(object value)
        {
            if (value == null || value == DBNull.Value) return string.Empty;

            var s = value.ToString();   // uses the server's current culture
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            {
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
