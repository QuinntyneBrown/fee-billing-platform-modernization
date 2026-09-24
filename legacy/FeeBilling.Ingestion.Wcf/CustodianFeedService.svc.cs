using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using FeeBilling.Data;
using log4net;

namespace FeeBilling.Ingestion.Wcf
{
    public class CustodianFeedService : ICustodianFeedService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CustodianFeedService));

        private readonly FeeBillingEntities _db = new FeeBillingEntities();

        // Fixed-width layout (all custodians, per the 2014 spec):
        //   0-11  account number   12-39 account name   40-57 market value   58-67 as-of date
        public FeedReceipt SubmitPositionFile(string custodianCode, string fileName, byte[] content)
        {
            var lines = Encoding.Default.GetString(content).Split('\n');   // Encoding.Default differs on .NET Core
            var positions = lines.Skip(1).Select(l => new Position
            {
                AccountNumber = l.Substring(0, 12).Trim(),
                MarketValue   = decimal.Parse(l.Substring(40, 18)),        // culture-dependent; fr-CA servers break
                AsOfDate      = DateTime.Parse(l.Substring(58, 10))        // same
            }).ToList();

            var formatter = new BinaryFormatter();                         // removed in .NET 9
            using (var ms = new MemoryStream())
            {
                formatter.Serialize(ms, positions);
                _db.StagedBatches.Add(new StagedBatch { Payload = ms.ToArray(), Status = "Received" });
            }
            _db.SaveChanges();
            return new FeedReceipt { Accepted = positions.Count };
        }

        public BatchStatus GetBatchStatus(int batchId)
        {
            var batch = _db.StagedBatches.Find(batchId);
            if (batch == null) return null;

            return new BatchStatus
            {
                BatchId = batch.Id,
                Status = batch.Status,
                ReceivedOn = batch.ReceivedOn,
                ProcessedOn = batch.ProcessedOn
            };
        }

        public int ProcessPendingBatches()
        {
            var processed = 0;
            var pending = _db.StagedBatches.Where(b => b.Status == "Received").OrderBy(b => b.Id).ToList();

            foreach (var batch in pending)
            {
                try
                {
                    List<Position> positions;
                    var formatter = new BinaryFormatter();
                    using (var ms = new MemoryStream(batch.Payload))
                    {
                        positions = (List<Position>)formatter.Deserialize(ms);   // trusts whatever is in the column
                    }

                    foreach (var p in positions)
                    {
                        // Custodian files only carry a total per account; store it as a single holding.
                        _db.Database.ExecuteSqlCommand(
                            "EXEC dbo.usp_UpsertPosition @AccountNumber, @AsOfDate, @SecurityCode, @MarketValue, @SourceBatchId",
                            new SqlParameter("@AccountNumber", p.AccountNumber),
                            new SqlParameter("@AsOfDate", p.AsOfDate),
                            new SqlParameter("@SecurityCode", "CUSTODIAN-TOTAL"),
                            new SqlParameter("@MarketValue", p.MarketValue),
                            new SqlParameter("@SourceBatchId", batch.Id));
                    }

                    batch.Status = "Processed";
                    batch.ProcessedOn = DateTime.Now;
                    processed++;
                }
                catch (Exception ex)
                {
                    // One bad line fails the whole batch.
                    Log.Error("Batch " + batch.Id + " failed", ex);
                    batch.Status = "Failed";
                }
                _db.SaveChanges();
            }

            return processed;
        }
    }
}
