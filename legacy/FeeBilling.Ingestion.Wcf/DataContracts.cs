using System;
using System.Runtime.Serialization;

namespace FeeBilling.Ingestion.Wcf
{
    [DataContract(Namespace = "http://schemas.feebilling.example/custodian/2014/01")]
    public class FeedReceipt
    {
        [DataMember]
        public int Accepted { get; set; }

        [DataMember]
        public int BatchId { get; set; }
    }

    [DataContract(Namespace = "http://schemas.feebilling.example/custodian/2014/01")]
    public class BatchStatus
    {
        [DataMember]
        public int BatchId { get; set; }

        [DataMember]
        public string Status { get; set; }

        [DataMember]
        public DateTime ReceivedOn { get; set; }

        [DataMember]
        public DateTime? ProcessedOn { get; set; }
    }

    /// <summary>
    /// One parsed line of a custodian position file. Staged as a BinaryFormatter-serialized
    /// List&lt;Position&gt; in StagedBatches.Payload - the assembly-qualified type name
    /// (FeeBilling.Ingestion.Wcf.Position, FeeBilling.Ingestion.Wcf) is baked into every row.
    /// </summary>
    [Serializable]
    public class Position
    {
        public string AccountNumber { get; set; }
        public decimal MarketValue { get; set; }
        public DateTime AsOfDate { get; set; }
    }
}
