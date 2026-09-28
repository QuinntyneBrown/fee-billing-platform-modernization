# 17 · WCF to CoreWCF

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-05 (transport) · **Prerequisites:** 01, 09

## Why this video exists

Modern .NET has no WCF *server*. FeeBilling receives custodian position files through a WCF `basicHttpBinding` service, and three client firms run an on-prem feed agent that can't be upgraded for about six months. The interface comment is blunt: **THE CONTRACT IS FROZEN**. "How do you handle WCF?" is one of the most common Framework-migration interview questions. The strong answer depends on who owns the clients: CoreWCF when you can't change them, REST or gRPC when you can. This video goes further than naming CoreWCF. It shows exactly which parts of a SOAP contract are on the wire, hosts the same contract on .NET 10, changes what happens *behind* it, and proves byte-level compatibility before any agent is pointed at it. Parsing, encoding and `BinaryFormatter` are video 18.

## Learning objectives

By the end, the viewer can:

- Say what was removed (WCF server hosting) and what is still supported (WCF client libraries), and choose between CoreWCF, REST and gRPC.
- List everything that makes up a SOAP contract on the wire: namespaces, contract and operation names, parameter names, data contract names and member order, SOAPAction, address and message size.
- Host `ICustodianFeedService` on CoreWCF with an equivalent `BasicHttpBinding`, reader quotas, request-size limits at every hop, and metadata.
- Change the implementation behind the contract (store to Blob Storage, parse in a worker) without changing what agents see, and decide consciously about the legacy quirks agents might depend on.
- Prove compatibility with a WSDL diff, golden SOAP envelopes, and a legacy-style client test.
- Plan the shim's retirement.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| How do you handle WCF in a migration? | Split by who owns the clients. External or frozen clients: CoreWCF, exposing the same contract, as a temporary adapter with a retirement date. Clients you control: REST or gRPC. WCF *client* code keeps working through the `System.ServiceModel.*` packages. |
| CoreWCF vs gRPC vs REST? | CoreWCF: compatibility with existing SOAP clients, not a destination. gRPC: internal service-to-service, contract-first, streaming, HTTP/2, not directly browser-friendly. REST: external partners and simple clients. For large file uploads, often a pre-signed blob upload rather than any RPC. |
| How do you keep a SOAP contract byte-compatible? | Keep the namespace, the interface name (it's in the SOAPAction), operation and parameter names, data contract names and member order, and the binding and address. Diff the WSDL. Replay captured real request envelopes and compare the responses. |
| What surprised you moving WCF to CoreWCF? | Size limits at every hop: base64 makes a 50 MB SOAP message hold roughly a 37 MB file, and Kestrel's default request-body limit is about 28.6 MB. Debug faults left on. `web.config` behaviours have to become code. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Ingestion.Wcf/ICustodianFeedService.cs` | "THE CONTRACT IS FROZEN", the namespace, the three operations, `ProcessPendingBatches` called by a scheduled task on APP01 |
| `legacy/FeeBilling.Ingestion.Wcf/DataContracts.cs` | `FeedReceipt`, `BatchStatus` data contracts in the same namespace |
| `legacy/FeeBilling.Ingestion.Wcf/Web.config` | `LargeFileBinding`, `readerQuotas`, `security mode="None"`, `bindingNamespace`, `includeExceptionDetailInFaults="true"` |
| `legacy/FeeBilling.Ingestion.Wcf/CustodianFeedService.svc` | The `.svc` path agents are configured with |
| `legacy/FeeBilling.Ingestion.Wcf/CustodianFeedService.svc.cs` | `SubmitPositionFile` never sets `BatchId`, `CustodianCode` or `FileName` |
| `seed/custodian-files/NBIN_20260930_POS.txt` | A real payload for the compatibility test (12 accounts) |
| `src/FeeBilling.Ingestion.CoreWcf/`, `src/FeeBilling.Ingestion.Worker/` (future, WP-05) | Target projects |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Three firms, an agent nobody can upgrade for six months, and a frozen contract. Rewriting the endpoint as REST isn't an option; leaving it on IIS isn't either. |
| 01:30–04:00 | What's gone and what isn't | No `ServiceHost`, no `.svc` activation in ASP.NET Core. WCF client packages are supported. The decision rule: who controls the clients? CoreWCF as an adapter, with a retirement date written in the ADR. |
| 04:00–07:30 | What "the contract" really is | Walk the SOAP request below. Namespace, the *interface* name inside SOAPAction, operation and parameter names as XML elements, `DataMember` names and default alphabetical order, `bindingNamespace`, the `.svc` address, the message-size limit. Renaming a C# parameter breaks an agent. |
| 07:30–12:00 | The CoreWCF host | Walk through `Program.cs`: `AddServiceModelServices`, `AddServiceModelMetadata`, `UseServiceModel`, a `BasicHttpBinding` in code with matching quotas and namespace, the Kestrel body limit, metadata over HTTP GET, debug faults left at their default (off), `UseRequestHeadersForMetadataAddressBehavior` behind the gateway. |
| 12:00–14:30 | New behaviour behind an old contract | `SubmitPositionFile` now writes the raw bytes to Blob Storage and returns; parsing moves to a worker. Decisions: what `Accepted` means now, whether to start returning a real `BatchId`, and `ProcessPendingBatches` becoming a compatibility no-op. |
| 14:30–17:00 | Proving compatibility | WSDL diff (legacy vs CoreWCF). Golden envelopes captured from a real agent, replayed with `curl`. A `ChannelFactory` test using the legacy interface, sending the seed file. The legacy SOAP client test is WP-05's acceptance criterion. |
| 17:00–18:30 | Security and hosting | `security mode="None"`: plain HTTP, no authentication, and whoever can post a position file can change billable AUM. TLS at the gateway if agents can switch URL by configuration; IP allowlisting per firm otherwise. No stack traces in faults. |
| 18:30–20:00 | Retirement and recap | Measure calls per firm to know when agents have moved. The replacement endpoint (for example a pre-signed blob upload plus a REST notification). Recap: same wire, new insides, a date to switch it off. |

### The contract on the wire

```csharp
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
```

What an agent actually sends (SOAP 1.1, `basicHttpBinding`):

```http
POST /CustodianFeedService.svc HTTP/1.1
Content-Type: text/xml; charset=utf-8
SOAPAction: "http://schemas.feebilling.example/custodian/2014/01/ICustodianFeedService/GetBatchStatus"

<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
  <s:Body>
    <GetBatchStatus xmlns="http://schemas.feebilling.example/custodian/2014/01">
      <batchId>1</batchId>
    </GetBatchStatus>
  </s:Body>
</s:Envelope>
```

| On the wire | Comes from | Breaks if you... |
|---|---|---|
| `http://schemas.feebilling.example/custodian/2014/01` | `ServiceContract.Namespace`, `DataContract.Namespace`, `bindingNamespace` | "tidy up" the namespace |
| `ICustodianFeedService` in SOAPAction | The interface name (default contract name) | rename the interface |
| `GetBatchStatus`, `batchId` | Method and parameter names | rename either |
| `Accepted`, `BatchId` elements and their order | `DataMember` names, default alphabetical order | rename or reorder members |
| `/CustodianFeedService.svc` | The endpoint address | move the path without a gateway route |
| Up to 50 MB messages | `maxReceivedMessageSize`, `readerQuotas` | keep the defaults |

### The CoreWCF host

The contract file is copied as-is, with `using System.ServiceModel;` replaced by `using CoreWCF;`. The data contracts use `System.Runtime.Serialization` and don't change.

```csharp
// src/FeeBilling.Ingestion.CoreWcf/Program.cs (future project, WP-05)
using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Description;

const int MaxMessageBytes = 52_428_800;   // same as legacy LargeFileBinding

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxMessageBytes);   // Kestrel default is ~28.6 MB
builder.Services.AddServiceModelServices();
builder.Services.AddServiceModelMetadata();
builder.Services.AddSingleton<IServiceBehavior, UseRequestHeadersForMetadataAddressBehavior>();   // WSDL shows the public host
builder.Services.AddTransient<CustodianFeedService>();   // constructor injection (BlobContainerClient, ILogger); check CoreWCF's instancing rules
builder.AddServiceDefaults();

var app = builder.Build();

app.UseServiceModel(services =>
{
    var binding = new BasicHttpBinding(BasicHttpSecurityMode.None)   // TLS terminates at the gateway
    {
        MaxReceivedMessageSize = MaxMessageBytes,
        MaxBufferSize = MaxMessageBytes,
        Namespace = "http://schemas.feebilling.example/custodian/2014/01",   // legacy bindingNamespace
    };
    binding.ReaderQuotas.MaxArrayLength = MaxMessageBytes;
    binding.ReaderQuotas.MaxStringContentLength = MaxMessageBytes;

    services.AddService<CustodianFeedService>();   // IncludeExceptionDetailInFaults stays at its default: false
    services.AddServiceEndpoint<CustodianFeedService, ICustodianFeedService>(binding, "/CustodianFeedService.svc");

    app.Services.GetRequiredService<ServiceMetadataBehavior>().HttpGetEnabled = true;   // agents fetch ?wsdl
});

app.MapDefaultEndpoints();
app.Run();
```

```csharp
public FeedReceipt SubmitPositionFile(string custodianCode, string fileName, byte[] content)
{
    var blobName = $"{custodianCode}/{_clock.GetUtcNow():yyyyMMdd}/{Guid.NewGuid():N}-{Path.GetFileName(fileName)}";
    _container.GetBlobClient(blobName).Upload(new BinaryData(content));   // the worker parses it (video 18)

    return new FeedReceipt
    {
        Accepted = CountDataLines(content),   // legacy semantics: lines after the header
        // BatchId: legacy always returned 0. Returning a real id is additive, but decide it explicitly.
    };
}
```

A Task-based server operation (`Task<FeedReceipt> SubmitPositionFileAsync(...)`) normally produces the same operation name on the wire, because WCF strips the `Async` suffix. That lets the upload be asynchronous without touching agents, but confirm it with the WSDL diff before relying on it.

### Legacy quirks agents may depend on

| Quirk | Legacy behaviour | Decision to record |
|---|---|---|
| `FeedReceipt.BatchId` | Never set: always `0` | Return the real id? Wire-compatible, but an agent that calls `GetBatchStatus(0)` today gets `null` and may treat that as "done" |
| `Accepted` | Number of lines after the header; any malformed line throws, so the agent gets a SOAP fault | With quarantine (video 18) a bad line no longer faults. That's a behaviour change: sign off, and tell the three firms |
| `ProcessPendingBatches` | Parses all staged batches, called every 15 minutes by `FeedProcessor` on APP01 | Keep the operation; make it a no-op that returns `0` and logs a metric, until the scheduled task is switched off |
| Faults | Full exception details and stack traces | Stop leaking them; keep the fault *shape* agents expect |

## Demo

```bash
docker compose up -d        # Azurite provides Blob Storage locally

# Scaffold (verify the template package name and short name before recording)
dotnet new install CoreWCF.Templates
dotnet new corewcf -n FeeBilling.Ingestion.CoreWcf -o src/FeeBilling.Ingestion.CoreWcf --dry-run

# WSDL diff: legacy (IIS Express on Windows) vs CoreWCF. Expect differences only in addresses.
curl -s "http://localhost:<legacy-port>/CustodianFeedService.svc?singleWsdl" > legacy.wsdl
curl -s "http://localhost:5200/CustodianFeedService.svc?wsdl" > corewcf.wsdl
diff <(xmllint --format legacy.wsdl) <(xmllint --format corewcf.wsdl)

# Replay a raw envelope, exactly as an agent sends it
curl -s http://localhost:5200/CustodianFeedService.svc \
  -H 'Content-Type: text/xml; charset=utf-8' \
  -H 'SOAPAction: "http://schemas.feebilling.example/custodian/2014/01/ICustodianFeedService/GetBatchStatus"' \
  -d '<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><GetBatchStatus xmlns="http://schemas.feebilling.example/custodian/2014/01"><batchId>1</batchId></GetBatchStatus></s:Body></s:Envelope>'
```

The acceptance test, written with the *legacy* contract and the supported WCF client package (`System.ServiceModel.Http`), against the CoreWCF host running on a real port:

```csharp
[Fact]
public async Task LegacyAgentContract_SubmitsSeedFile()
{
    var binding = new System.ServiceModel.BasicHttpBinding { MaxReceivedMessageSize = 52_428_800 };
    var factory = new System.ServiceModel.ChannelFactory<ICustodianFeedService>(
        binding, new System.ServiceModel.EndpointAddress($"{_host.BaseAddress}CustodianFeedService.svc"));
    var agent = factory.CreateChannel();

    var content = await File.ReadAllBytesAsync(SeedFiles.Path("NBIN_20260930_POS.txt"));
    var receipt = agent.SubmitPositionFile("NBIN", "NBIN_20260930_POS.txt", content);

    Assert.Equal(12, receipt.Accepted);
}
```

Also generate a client from the new host's WSDL with `dotnet-svcutil` and check that it matches one generated from the legacy WSDL.

## Traps to call out

- **"Same interface, so it's compatible."** Only if names, namespaces and order survived. The interface name itself is inside every SOAPAction.
- **Size limits at every hop.** Base64 inflates `byte[]` by a third, so a 50 MB message carries at most about 37 MB of file. Kestrel's default body limit (about 28.6 MB), the gateway's limit, and any load balancer all have to agree. Legacy IIS request filtering has its own default too, so measure the largest real file before promising a number.
- **Leaving `includeExceptionDetailInFaults` on "for compatibility".** Agents need the fault shape, not the stack trace.
- **Fixing quirks silently.** A real `BatchId`, or accepting files with bad lines, is a behaviour change for three firms' automation. Record it and tell them.
- **Treating CoreWCF as the destination.** It's an adapter with a retirement date. Track calls per firm so you know when you can switch it off.
- **`security mode="None"` on the internet.** Anyone who can reach the endpoint can change positions, and therefore fees. At minimum put TLS and IP allowlisting in front of it.
- **Assuming support status.** Check CoreWCF's current support policy and version before recording.

## Key terms

WCF · CoreWCF · `basicHttpBinding` · SOAP 1.1 · SOAPAction · WSDL · data contract · reader quotas · MTOM (not used here) · golden envelope · adapter · retirement date · pre-signed (SAS) upload

## After the video

1. Write the ADR: CoreWCF shim for the three frozen agents, the retirement criteria, and the decisions on `BatchId`, `Accepted` and `ProcessPendingBatches`.
2. Capture (or hand-write from the WSDL) golden request and response envelopes for all three operations and turn them into replay tests.
3. Sketch the replacement upload API that agents move to after the six months: authentication, pre-signed upload, notification, status polling.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (Ingestion), Section 9 WP-05, Section 12.2 ("How do you handle WCF?"), Section 13
- `docs/handover.md`: "Three client firms' feed agents can't be upgraded for ~6 months"
- CoreWCF project documentation and samples (GitHub: CoreWCF/CoreWCF)
- Microsoft Learn: *WCF client support in .NET*, *dotnet-svcutil*, *Kestrel limits*
