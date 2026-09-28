# 17 · WCF to CoreWCF

Welcome. This lesson is about one of the most common questions in any .NET Framework migration interview: how do you handle WCF? Modern .NET has no WCF server, and FeeBilling receives custodian position files through a WCF service that three client firms can't stop using for about six months. By the end of this lesson, you should be able to explain what was removed and what still works, choose between CoreWCF, REST and gRPC, list exactly what makes a SOAP contract compatible on the wire, host the same contract on .NET 10, change what happens behind it, and prove compatibility before any real client is pointed at it. Parsing, encoding and BinaryFormatter are the next lesson.

## The questions this lesson answers

Here's what you'll be ready for. How do you handle WCF in a migration? CoreWCF versus gRPC versus REST? How do you keep a SOAP contract byte-compatible? And a war-story question: what surprised you moving WCF to CoreWCF?

The strong answer to the first question starts with a question of its own: who owns the clients? Everything else follows from that.

## What's gone, and what isn't

Let's be precise, because vague answers here are common. What modern .NET removed is WCF server hosting: `ServiceHost`, `.svc` activation in IIS, and the configuration system in `web.config` that drives it. There's no built-in way to host a WCF service in ASP.NET Core.

What's still supported is the WCF client. If your code calls someone else's SOAP service, the `System.ServiceModel` client packages, like `System.ServiceModel.Http`, work on modern .NET, and `dotnet-svcutil` generates client proxies from WSDL. So a migration that only consumes SOAP services has much less to worry about.

For the server side, there are three options. CoreWCF is a community-maintained port of the WCF server to ASP.NET Core, and Microsoft has contributed to and supported it, but check its current support policy and version before you rely on specifics. It hosts the same service contracts and bindings, so existing SOAP clients keep working. gRPC is a contract-first RPC framework over HTTP/2, great for internal service-to-service calls and streaming, but not directly browser-friendly and not something a SOAP client can call. And REST is the default for external partners and simple clients.

The decision rule is simple. If you can't change the clients, because they're external, or frozen, use CoreWCF to expose the same contract, as a temporary adapter with a retirement date. If you control the clients, move them to REST or gRPC. And for large file uploads specifically, the best long-term answer is often neither: it's a pre-signed upload directly to blob storage, followed by a small notification call.

## The frozen contract in FeeBilling

Now the repository. Open `legacy/FeeBilling.Ingestion.Wcf/ICustodianFeedService.cs`. The comment above the interface says it's called by the on-prem feed agent installed at client firms, which pushes daily custodian position files over SOAP with basic HTTP binding. And then, in capitals: the contract is frozen. Three firms run agent versions that cannot be upgraded. Namespace, operation names and parameter names must not change.

The interface has three operations. `SubmitPositionFile` takes a custodian code, a file name and the file content as a byte array, and returns a `FeedReceipt`. `GetBatchStatus` takes a batch ID and returns a `BatchStatus`. And `ProcessPendingBatches` takes nothing and returns an integer. Its comment says it's invoked every fifteen minutes by the FeedProcessor scheduled task on a server called APP01. That's an undocumented dependency outside this repository, and you'll need to know about it at cutover.

The data contracts are in `DataContracts.cs`, in the same namespace. `FeedReceipt` has two members, `Accepted` and `BatchId`. `BatchStatus` has the batch ID, status, and received and processed times.

Then open `legacy/FeeBilling.Ingestion.Wcf/Web.config`. There's a binding called `LargeFileBinding`. It raises the maximum received message size to 52,428,800 bytes, which is fifty megabytes, raises the reader quotas for array length and string content to the same, and sets security mode to None. The endpoint uses basic HTTP binding with a binding namespace that matches the contract namespace. There's a metadata endpoint so agents can fetch the WSDL. And there's a service behaviour with `includeExceptionDetailInFaults` set to true, with a comment saying it was left on after a 2016 production incident, and that it leaks stack traces to the agents.

Put those facts together and you can see the bind the team is in. Rewriting the endpoint as REST isn't an option, because the agents can't change for six months. Leaving it on IIS isn't an option either, because the whole point of the migration is to leave Windows-only hosting, and every month on IIS keeps the old ingestion code, with its BinaryFormatter staging, in production. So the answer has to keep the wire exactly the same while replacing everything behind it.

Finally, `CustodianFeedService.svc` is the address the agents are configured with, and `CustodianFeedService.svc.cs` is the implementation. Remember two quirks in it for later. `SubmitPositionFile` returns a receipt with only `Accepted` set, so `BatchId` is always zero. And the staged batch it saves never records the custodian code or the file name.

## What the contract really is on the wire

Here's the insight that separates a strong answer: the C# interface isn't the contract. The contract is the XML on the wire, and several things you might think are cosmetic are actually in it.

Picture what an agent sends for `GetBatchStatus`. It's an HTTP POST to the path ending in `CustodianFeedService.svc`, with a content type of text XML and a SOAPAction header. The SOAPAction value is the namespace, followed by the interface name, `ICustodianFeedService`, followed by the operation name. The body is a SOAP envelope containing an element named `GetBatchStatus`, in the contract namespace, with a child element named `batchId`.

So look at what's on the wire. The namespace, which comes from the service contract attribute, the data contract attributes, and the binding namespace. Tidy it up and every agent breaks. The interface name, because it's the default contract name and it appears in every SOAPAction. Rename the interface and every agent breaks. Operation and parameter names, because they become XML element names. Renaming a C# parameter from `batchId` to `id` looks like a harmless refactoring, and it breaks an agent. Data member names and their order: with no explicit order, the data contract serializer writes members alphabetically, so `Accepted` comes before `BatchId`. Rename or reorder them and responses change. The address, the path ending in `.svc`. And the maximum message size.

That list is your answer to "how do you keep a SOAP contract byte-compatible?"

## Hosting the contract on CoreWCF

Now the new host, a future project called `FeeBilling.Ingestion.CoreWcf`. The contract file is copied as-is, with the using directive for `System.ServiceModel` replaced by `CoreWCF`. The data contracts use `System.Runtime.Serialization`, which exists in modern .NET, so they don't change at all.

In `Program.cs`, it's an ordinary ASP.NET Core application. You call `AddServiceModelServices` and `AddServiceModelMetadata` on the service collection, register the service class so it can use constructor injection for things like a blob container client and a logger, and then call `UseServiceModel` on the app. Inside that, you add the service, and add an endpoint for the service and the contract interface, with a binding and the same `.svc` path agents already use.

Everything `web.config` used to say now has to be said in code. You create a `BasicHttpBinding` with security mode None, because TLS terminates at the gateway. You set `MaxReceivedMessageSize` and `MaxBufferSize` to the same fifty megabytes. You set the reader quotas for maximum array length and maximum string content length. And you set the binding's `Namespace` property to the legacy binding namespace. Then you enable metadata over HTTP GET, so agents can still fetch the WSDL. If the service sits behind the YARP gateway, add the behaviour that uses request headers for the metadata address, so the WSDL shows the public host name rather than an internal one.

Two more things. First, leave exception details in faults at the default, which is off. Agents need the fault shape, not the stack trace. Second, size limits apply at every hop, and this is the war story interviewers enjoy. The file content is a byte array, and SOAP sends byte arrays as base64 text, which inflates them by a third. So a fifty-megabyte message carries a file of at most about thirty-seven megabytes. And Kestrel, the ASP.NET Core web server, has its own maximum request body size, which defaults to thirty million bytes, about 28.6 megabytes. You have to raise it in the Kestrel configuration. Then the gateway, and any load balancer in front of it, need matching limits too. Measure the largest real file before you promise a number.

One optional refinement: you can make the operation asynchronous on the server by returning a task and naming the method with an Async suffix. WCF normally strips that suffix, so the operation name on the wire stays the same. Confirm it with a WSDL diff before relying on it.

## New behaviour behind an old contract

Here's the point of the whole exercise: the contract stays frozen, but everything behind it can change.

In the new host, `SubmitPositionFile` does very little. It writes the raw bytes to Blob Storage, under a name that includes the custodian code, the date and a unique ID, and returns. Azurite provides Blob Storage locally in `docker-compose.yml`. Parsing moves to a separate worker, which is where the culture and encoding fixes from the next lesson live. No more `BinaryFormatter` staging in a database column.

But now you have decisions to make, and each one should be recorded, because agents might depend on the old behaviour.

What does `Accepted` mean? Legacy returns the number of lines after the header. For the NBIN seed file dated September 30, 2026, in `seed/custodian-files`, that's twelve. And today any malformed line throws, so the agent gets a SOAP fault. With the quarantine flow from the next lesson, a bad line no longer faults the whole file. That's a behaviour change for three firms' automation. Get sign-off and tell them.

What about `BatchId`? Legacy always returns zero. Returning a real ID is wire-compatible, because the element is already there. But an agent that calls `GetBatchStatus` with zero today gets null back and may treat that as "done". Decide explicitly, don't fix it silently.

And `ProcessPendingBatches`? In the new design, a worker processes files as they arrive, so the operation has nothing to do. But the scheduled task on APP01 still calls it every fifteen minutes. Keep the operation, make it a no-op that returns zero and records a metric, and switch the scheduled task off as part of cutover.

## Proving compatibility

How do you prove all this before pointing a real agent at it? Three layers.

First, a WSDL diff. Fetch the legacy WSDL with the single WSDL query string, fetch the new one, format both, and diff them. The only differences should be addresses.

Second, golden envelopes. Capture real request envelopes from an agent, or hand-write them from the WSDL, for all three operations. Replay them with curl against the new host, with the right content type and SOAPAction header, and compare the responses with what legacy returns.

Third, a legacy-style client test, which is the work package's acceptance criterion. Write an integration test that uses the supported WCF client package and the legacy interface: create a `ChannelFactory` for `ICustodianFeedService` with a matching basic HTTP binding, point it at the CoreWCF host running on a real port, read the seed file, call `SubmitPositionFile`, and assert that `Accepted` is twelve. If that passes, an agent built against the old contract can talk to the new host. You can also generate a client from each WSDL with `dotnet-svcutil` and check they match.

Run all three layers in CI, not just once before go-live. The contract is frozen for at least six months, and a well-meaning refactoring in month four is exactly when a parameter gets renamed. A failing WSDL diff in a pull request is a much cheaper way to find that out than a phone call from a client firm whose positions stopped loading.

## Security, hosting and retirement

Now the uncomfortable part. Security mode None means plain HTTP and no authentication. Whoever can reach the endpoint can post a position file, which changes billable AUM, which changes fees. At minimum, terminate TLS at the gateway, if the agents can switch URL by configuration, and put IP allowlisting per firm in front of it. And stop leaking stack traces.

Finally, retirement. CoreWCF here is an adapter, not a destination. Put a retirement date in the ADR. Measure calls per firm, so you know when each agent has moved to the replacement, which might be a pre-signed blob upload plus a REST notification, with proper authentication. When the three firms have upgraded, the shim goes, and it's on the decommissioning checklist in lesson twenty-three.

## Traps

A few traps to call out.

Assuming the same interface means compatible. Only if names, namespaces and order survived. The interface name itself is in every SOAPAction.

Forgetting size limits at every hop. Base64 inflates by a third, and Kestrel's default body limit is about 28.6 megabytes.

Leaving exception details on for compatibility. Keep the fault shape, drop the stack trace.

Fixing quirks silently. A real batch ID, or accepting files with bad lines, changes three firms' automation.

Treating CoreWCF as the destination. It's an adapter with a date.

And leaving security mode None exposed to the internet.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** How do you handle WCF in a migration?

[pause 5s]

I split it by who owns the clients. If the clients are external or frozen, like FeeBilling's feed agents that can't be upgraded for six months, I host the same contract on CoreWCF, as a temporary adapter with a retirement date, and change what happens behind it. If I control the clients, I move them to REST or gRPC. WCF client code, where we call other people's SOAP services, keeps working through the `System.ServiceModel` client packages.

**Interviewer:** CoreWCF versus gRPC versus REST?

[pause 5s]

CoreWCF is for compatibility with existing SOAP clients, not a destination. gRPC is for internal service-to-service calls: contract-first, fast, supports streaming, runs on HTTP/2, but not directly browser-friendly. REST is for external partners and simple clients. And for large file uploads, often none of them: a pre-signed upload to blob storage plus a notification.

**Interviewer:** How do you keep a SOAP contract byte-compatible?

[pause 5s]

Keep everything that's on the wire: the namespace, the interface name, because it's in the SOAPAction, operation and parameter names, data member names and their order, the binding namespace, the address and the message size limits. Then prove it: diff the WSDL, replay captured request envelopes and compare responses, and run a test that uses the legacy contract through a WCF client against the new host.

**Interviewer:** What surprised you moving WCF to CoreWCF?

[pause 5s]

Size limits at every hop. Byte arrays go over SOAP as base64, so a fifty-megabyte message holds about a thirty-seven-megabyte file, and Kestrel's default request body limit is about 28.6 megabytes, so uploads that worked on IIS failed until every hop was configured. Also, everything in `web.config`, bindings, quotas and behaviours, has to be expressed in code, and we found debug faults had been left on for years, leaking stack traces.

## Recap

Five things to remember from this lesson.

One: modern .NET removed WCF server hosting, not WCF clients. The decision rule is who owns the clients: CoreWCF for clients you can't change, REST or gRPC for clients you can.

Two: the contract is the XML on the wire. Namespace, interface name in the SOAPAction, operation and parameter names, data member names and order, address and size limits.

Three: on CoreWCF, everything `web.config` said moves into code: the binding, quotas, namespace and metadata. And raise the Kestrel body limit, because base64 inflates the payload.

Four: keep the contract frozen and change the inside. Store to Blob Storage and parse in a worker, and record every decision about legacy quirks like the zero batch ID.

Five: prove compatibility with a WSDL diff, golden envelopes and a legacy client test, secure the endpoint, and give the shim a retirement date.

In the next lesson, we'll fix what happens after the file arrives: BinaryFormatter staging, text encoding, and culture-dependent parsing, the things that break silently.
