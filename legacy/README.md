# FeeBilling (legacy, as found)

.NET Framework 4.7.2 solution, 2011–2016. Maintained, not evolved. Everything here is intentionally left the way the modernization team found it. **Don't refactor it before its behaviour has been captured** (see WP-01 in the training plan).

| Project | What it is | Runs locally? |
|---|---|---|
| `FeeBilling.Web` | ASP.NET MVC 5 + Web API 2 on `System.Web`, AngularJS 1.6 SPA under `App/`, Unity DI, log4net, Forms auth with custom membership | Compiles. Needs IIS / IIS Express to host |
| `FeeBilling.Core` | Business logic: static `FeeCalculator`, `BlendedFeeCalculator`, `FlatFeeCalculator`, `AumService`, `HouseholdFeeService`, `HouseholdAllocator`, `BillingRunService` | **Yes**, on Windows, against the docker database |
| `FeeBilling.Data` | EF6 Database-First (`FeeBilling.edmx`), `ReportingEntities` (second database), ADO.NET `InvoiceRepository`, `DbContextFactory` | **Yes**, on Windows |
| `FeeBilling.Ingestion.Wcf` | WCF `CustodianFeedService.svc` (basicHttpBinding), called by the on-prem feed agent | Compiles. Needs IIS for WCF hosting |
| `FeeBilling.BillingRunner` | Windows Service that polls `BillingRunQueue` every 30 s | Compiles. Needs MSDTC (two databases in one `TransactionScope`) |
| `FeeBilling.Invoicing` | PDF invoices via `System.Drawing` + a Framework-only PDF library | Compiles |
| `FeeBilling.Tests` | MSTest. 61 tests: 30 pass, 9 fail, 22 ignored | Yes (`dotnet test`) |

`FeeBilling.sln` also includes `../src/FeeBilling.Domain` (`netstandard2.0`), which the modernization team extracted and `FeeBilling.Core` now references.

## Build and test

```bash
dotnet build legacy/FeeBilling.sln
dotnet test  legacy/FeeBilling.Tests      # expect: Failed 9, Passed 30, Skipped 22
```

The build prints NuGet audit warnings for vulnerable packages. They are part of the "as found" state.

## Calling the legacy fee engine from your own code

`FeeCalculator` and friends get their `DbContext` from `HttpContext.Current.Items` (`DbContextFactory.Current`). Outside IIS there is no `HttpContext`, so the BillingRunner fakes one; see `FeeBilling.BillingRunner/FakeHttpContext.cs`. A console tool needs:

- a reference to `FeeBilling.Core` (net472) and `<Reference Include="System.Web" />`
- an `App.config` with the EF connection string:

```xml
<connectionStrings>
  <add name="FeeBillingEntities" providerName="System.Data.EntityClient"
       connectionString="metadata=res://*/FeeBilling.csdl|res://*/FeeBilling.ssdl|res://*/FeeBilling.msl;provider=System.Data.SqlClient;provider connection string=&quot;data source=localhost,1433;initial catalog=FeeBilling;user id=sa;password=FeeBilling!Passw0rd;MultipleActiveResultSets=True;App=EntityFramework&quot;" />
</connectionStrings>
```

- `HttpContext.Current = FakeHttpContext.Create();` on the calling thread before the first call. `HttpContext` doesn't flow to other threads.

The EF6 provider is registered in code (`FeeBillingDbConfiguration`), so no `<entityFramework>` section is needed.

## How this differs from the real thing

The real solution uses old-style `.csproj` files with `packages.config`, builds in Visual Studio, and deploys to IIS. To make it build with the `dotnet` CLI on a machine without Visual Studio:

- **SDK-style `net472` projects.** They compile against `Microsoft.NETFramework.ReferenceAssemblies`, so no targeting pack is needed. Package versions are pinned per project; there is no central package management here.
- **EDMX build step.** `FeeBilling.Data.csproj` splits `FeeBilling.edmx` into `.csdl`/`.ssdl`/`.msl` embedded resources with an `XmlPeek` target. This replaces Visual Studio's EntityDeploy task, which doesn't ship with the EF6 NuGet package.
- **T4 templates.** They are removed. The generated entity classes are checked in under `FeeBilling.Data/Model/`.
- **PDF library.** PDFsharp 1.50 (GDI+ build) stands in for the commercial, Framework-only PDF library. It's the same shape of problem: Framework-only and built on `System.Drawing`.
- **`Web.config` transforms.** `Web.Debug`, `Web.Release` and `Web.ClientX` are present but only applied by a publish, which nothing here runs.
