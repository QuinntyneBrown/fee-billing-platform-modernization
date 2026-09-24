using System.ServiceModel;

namespace FeeBilling.Ingestion.Wcf
{
    /// <summary>
    /// Called by the on-prem "feed agent" installed at client firms, which pushes the daily
    /// custodian position files over SOAP (basicHttpBinding).
    ///
    /// THE CONTRACT IS FROZEN: three firms run agent versions that cannot be upgraded.
    /// Namespace, operation names and parameter names must not change.
    /// </summary>
    [ServiceContract(Namespace = "http://schemas.feebilling.example/custodian/2014/01")]
    public interface ICustodianFeedService
    {
        [OperationContract]
        FeedReceipt SubmitPositionFile(string custodianCode, string fileName, byte[] content);

        [OperationContract]
        BatchStatus GetBatchStatus(int batchId);

        /// <summary>Invoked every 15 minutes by the FeedProcessor scheduled task on APP01.</summary>
        [OperationContract]
        int ProcessPendingBatches();
    }
}
