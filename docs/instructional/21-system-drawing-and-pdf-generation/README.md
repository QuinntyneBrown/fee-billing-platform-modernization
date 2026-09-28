# 21 · System.Drawing and PDF Generation

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** none directly (Invoicing is outside WP-01 to WP-10, but blocks containerization) · **Prerequisites:** 02, 03, 18

**Audio lesson:** [21-system-drawing-and-pdf-generation.mp3](21-system-drawing-and-pdf-generation.mp3) · [Transcript](script.md)

## Why this video exists

Every Framework estate has a Windows-only dependency that nobody owns. In FeeBilling it's `FeeBilling.Invoicing`: PDF invoices drawn with `System.Drawing` (GDI+) and a Framework-only PDF library. It's not money-calculating code, but it produces the one document clients actually read, and its tests have been ignored for years because "GDI+ not available on the build server". This video covers why `System.Drawing` doesn't move to Linux containers, three real bugs hiding in the renderer, how to pick a replacement library as a regulated vendor, and how to test PDFs without comparing bytes.

## Learning objectives

By the end, the viewer can:

- Explain why `System.Drawing.Common` is Windows-only in modern .NET and what that means for containers.
- Find the thread-safety, resource and culture bugs in a GDI+ renderer that "worked" on IIS.
- Evaluate a third-party library on platform, licence, maintenance, security and output requirements, and record the choice as an ADR.
- Redesign rendering behind the existing `IInvoicePdfRenderer` seam, with culture, currency and branding passed in explicitly.
- Get fonts and globalization right in a Linux container.
- Test PDF output by extracting text, and with visual snapshots.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What do you do with Windows-only dependencies? | Find them early (the `CA1416` platform analyzer, `git grep`, running on Linux in CI). Isolate them behind an interface. Choose between replacing them, keeping a Windows-hosted component for a while, or containerizing on Windows. Replacement is usually right for leaf components like PDF generation. |
| How do you evaluate a third-party library for a regulated vendor? | Licence model and revenue thresholds (and copyleft: an AGPL library in a SaaS product is a legal question). Maintenance cadence and bus factor. Pure managed vs native assets. CVE history. Output requirements such as PDF/A for archiving. Performance at run scale (40,000 invoices). Record it in an ADR with the licence review attached. |
| Why did this code work for years and fail now? | IIS on Windows hid it: GDI+ was present, the server culture was fixed, and concurrency was low. Containers change all three. |
| How do you test generated documents? | Extract text and assert on content. Use visual snapshots, with a tolerance, for layout. Never compare bytes: PDFs embed creation dates, document IDs and producer versions. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Invoicing/InvoicePdfRenderer.cs` | Static cached `Image _logo`; `GetLogo().Save(...)` outside the lock; `ToShortDateString()` and `ToString("C")` marked `// server culture` |
| `legacy/FeeBilling.Invoicing/IInvoicePdfRenderer.cs`, `InvoiceDocument.cs` | The seam that already exists; `InvoiceDocument` carries no culture or currency |
| `legacy/FeeBilling.Invoicing/FeeBilling.Invoicing.csproj` | `PDFsharp` `1.50.5147`, the GDI+ build standing in for the commercial Framework-only library |
| `legacy/README.md` | "How this differs from the real thing": the PDF library substitution |
| `legacy/FeeBilling.Web/Web.config` | `<globalization culture="auto" uiCulture="auto" />`; `Invoice.LogoPath`; `Invoice.FooterText` (read by nothing) |
| `legacy/FeeBilling.Web/App_Start/UnityConfig.cs` | `RegisterType<IInvoicePdfRenderer, InvoicePdfRenderer>()`: swapping implementations is one line |
| `legacy/FeeBilling.Web/Controllers/Api/InvoicesController.cs` | `GetInvoicePdf`: builds `InvoiceDocument` through lazy-loaded navigations |
| `legacy/FeeBilling.Tests/Invoicing/InvoicePdfRendererTests.cs` | Every test is `[Ignore("GDI+ not available on the build server")]` |
| `database/billing/001-schema.sql` | `Firms.Culture` (default `'en-CA'`) and `FeeSchedules.Currency` exist, but the renderer ignores both |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | The renderer's tests are all ignored, so nothing in CI has ever rendered an invoice. Laurentien is a `fr-CA` firm, but its invoices are formatted in whatever culture the server, or the browser that clicked the link, happens to have. |
| 01:30–04:30 | Why `System.Drawing` is Windows-only | It's a thin wrapper over GDI+, a Windows component. On Unix it relied on `libgdiplus`, which was incomplete. .NET 6 made `System.Drawing.Common` Windows-only, and .NET 7 removed the opt-in switch for Unix (verify wording before recording). The `CA1416` analyzer flags call sites. The commercial Framework-only PDF library has the same problem, and there's no newer version to upgrade to. |
| 04:30–08:00 | Code review: bugs IIS hid | (1) A static GDI+ `Image` is shared across requests; creation is locked, but `Save` isn't. GDI+ objects aren't thread-safe, so concurrent renders fail intermittently with "Object is currently in use elsewhere". (2) `Image.FromFile` keeps the file locked while the image is alive, so the logo can't be replaced without recycling the app pool. (3) `new Font(...)` and `new SolidBrush(...)` are never disposed, which leaks GDI handles. (4) Culture: `globalization culture="auto"` means the invoice's date and currency format follow the requesting browser's `Accept-Language`. |
| 08:00–12:00 | Choosing a replacement | Walk the evaluation table below. Licence thresholds change, so verify each one on the day. Copyleft is a trap in a SaaS product. Native assets (SkiaSharp) need the right Linux package in the image. HTML-to-PDF through headless Chromium is flexible but heavy. The deliverable is an ADR, not a preference. |
| 12:00–15:00 | Redesign behind the seam | Keep `IInvoicePdfRenderer`. `InvoiceDocument` gains `Culture` and `Currency`, sourced from `Firms.Culture` and `FeeSchedules.Currency`. Branding is loaded once as immutable bytes, and each render builds its own image objects. No `ConfigurationManager`, no statics, no server culture. |
| 15:00–18:00 | Containers: fonts and globalization | Linux images don't ship Arial, and Arial's licence may not allow you to add it. Embed licensed fonts through the library's font resolver, or install a font package in the image. Globalization: with no `LANG` set, `CurrentCulture` is invariant, and `"C"` prints the generic currency sign `¤`. Alpine and chiseled images default to invariant globalization with no ICU data. `fr-CA` group separators differ between Windows NLS and ICU. |
| 18:00–19:15 | Testing PDFs | Extract text (for example with PdfPig) and assert on the amount, period and account. Do text-level parity against legacy-rendered PDFs for the seed invoices. Take visual snapshots of a rendered page image with a tolerance. Run a concurrency test that renders 50 invoices in parallel. |
| 19:15–20:00 | Recap | Isolate, replace, and make culture explicit. Test content, not bytes. Every library choice is a licence decision. |

### Library evaluation (verify every licence and status before recording)

| Option | Platform | Licence (check current terms) | Notes |
|---|---|---|---|
| PDFsharp 6.x (Core build) | Cross-platform, managed | MIT | Closest to the legacy API, so the smallest code change. Needs a font resolver on Linux. API changes from 1.50 (for example, font style enums). |
| QuestPDF | Cross-platform | Community licence below a revenue threshold, commercial above it | Fluent layout API, good for tabular invoices. Check the threshold against the vendor's revenue. |
| SkiaSharp (+ its PDF backend) | Cross-platform, **native** assets | MIT | Needs `SkiaSharp.NativeAssets.Linux` (or a no-dependencies variant) in containers. Low-level drawing. |
| ImageSharp (for the logo only) | Cross-platform, managed | Six Labors Split Licence, commercial for some organizations | Only needed if you manipulate images; embedding a PNG usually isn't that. |
| HTML to PDF via headless Chromium (Playwright) | Cross-platform, large image | Apache-2.0 (Playwright) | Designers can edit templates. Costs: image size, cold start, sandboxing, and output that drifts with Chromium versions. |
| Copyleft PDF libraries (for example, AGPL editions) | Varies | AGPL or commercial | In a hosted product, AGPL obligations are a legal review item, not an engineering one. |

### Before: shared GDI+ state and server culture

```csharp
        // GDI+ objects cached for the lifetime of the app pool.
        private static Image _logo;
        private static readonly object LogoLock = new object();
```

```csharp
                using (var logoStream = new MemoryStream())
                {
                    GetLogo().Save(logoStream, ImageFormat.Png);
                    logoStream.Position = 0;
                    gfx.DrawImage(XImage.FromStream(logoStream), 40, 30, 120, 40);
                }
```

```csharp
                DrawLine(gfx, bodyFont, "Period ending", invoice.PeriodEnd.ToShortDateString(), ref y);   // server culture
                DrawLine(gfx, bodyFont, "Fee schedule", invoice.ScheduleCode, ref y);
                DrawLine(gfx, bodyFont, "Amount due", invoice.Amount.ToString("C"), ref y);                // server culture
```

```csharp
                        g.FillRectangle(new SolidBrush(Color.FromArgb(0, 70, 127)), 0, 0, 300, 100);
                        g.DrawString("FeeBilling", new Font("Arial", 28, FontStyle.Bold), Brushes.White, 20, 25);
```

### After: explicit inputs, no shared mutable state (sketch)

```csharp
public sealed record InvoiceDocument(
    int InvoiceId,
    string FirmName,
    string AccountNumber,
    string AccountName,
    string? HouseholdCode,
    DateOnly PeriodEnd,
    decimal Amount,
    string CurrencySymbol,     // from FeeSchedules.Currency, e.g. CAD -> "$"
    string ScheduleCode,
    CultureInfo Culture);      // from Firms.Culture: en-CA, fr-CA

public sealed record InvoiceBranding(ReadOnlyMemory<byte> LogoPng, string FooterText);

public sealed class InvoicePdfRenderer(InvoiceBranding branding) : IInvoicePdfRenderer
{
    public byte[] Render(InvoiceDocument invoice)
    {
        var numberFormat = (NumberFormatInfo)invoice.Culture.NumberFormat.Clone();
        numberFormat.CurrencySymbol = invoice.CurrencySymbol;

        var periodEnd = invoice.PeriodEnd.ToString("D", invoice.Culture);
        var amountDue = invoice.Amount.ToString("C", numberFormat);

        // Each render creates its own document, fonts and image from the immutable logo bytes.
        // Library-specific drawing goes here (PDFsharp 6 Core, QuestPDF, ...).
        return RenderWithLibrary(invoice, periodEnd, amountDue, branding.LogoPng.Span);
    }
}
```

`InvoiceBranding` is registered as a singleton, loaded once at startup from configuration or Blob Storage. It's safe to share because nothing mutates it. The legacy `Image` looked equally shareable, but it wasn't.

### After: testing content, not bytes (sketch)

```csharp
[Theory]
[InlineData("en-CA", "$5,312.50")]
[InlineData("fr-CA", "5 312,50 $")]   // normalize NBSP / narrow NBSP before comparing
public void Render_FormatsAmountInFirmCulture(string culture, string expectedAmount)
{
    var bytes = _renderer.Render(SampleInvoice(CultureInfo.GetCultureInfo(culture)));

    using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
    var text = string.Join(" ", pdf.GetPages().Select(page => page.Text));

    Assert.Contains(expectedAmount, NormalizeSpaces(text));
}
```

## Demo

```bash
# Where is System.Drawing, and which tests have never run?
git grep -n "System.Drawing" -- legacy src
git grep -n "GDI+ not available" -- legacy/FeeBilling.Tests

# Culture: the same amount under different cultures (a .NET 10 file-based app)
cat > culture.cs <<'EOF'
using System.Globalization;
foreach (var name in new[] { "en-CA", "fr-CA", "" })
{
    var c = CultureInfo.GetCultureInfo(name);
    Console.WriteLine($"{(name == "" ? "invariant" : name),-10} {5312.50m.ToString("C", c),-14} {new DateTime(2026, 9, 30).ToString("d", c)}");
}
Console.WriteLine($"current='{CultureInfo.CurrentCulture.Name}' {5312.50m:C}");
EOF
dotnet run culture.cs                                    # on Windows
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet run culture.cs   # in Linux
```

Compare the two runs on screen. In the container, expect `current=''` and a `¤` currency sign unless `LANG` is set, and different `fr-CA` spacing characters (verify on your base image). Delete `culture.cs` afterwards.

## Traps to call out

- **Leaving it on Windows "for now" without a date.** A Windows container or VM for one leaf component keeps Windows in the platform indefinitely. If it's a deliberate stopgap, give it a retirement date in the ADR.
- **Assuming a lock makes GDI+ safe.** The lock guarded *creation*, not *use*. Shared GDI+ objects need all access serialized, or better, no sharing at all.
- **Server culture in client documents.** `culture="auto"` made invoice formatting depend on who clicked. In a container, it depends on an environment variable nobody set. Culture is business data (`Firms.Culture`): pass it in.
- **Fonts.** "It rendered on my machine" usually means Windows had Arial. Embed licensed fonts; check the font licence permits embedding.
- **Byte-comparing PDFs.** Creation dates, document IDs and producer strings change every render. Compare extracted text and rendered images.
- **Licence drift.** Several popular .NET libraries changed licence terms in recent years. A regulated vendor records the licence review in the ADR, and re-checks it on major upgrades.
- **Dead configuration.** `Invoice.FooterText` exists in `Web.config`, but the renderer never reads it. Inventory what configuration is *used* (video 19), not just what exists.

## Key terms

GDI+ · `System.Drawing.Common` · `libgdiplus` · platform compatibility analyzer (`CA1416`) · native assets · font resolver / font embedding · invariant globalization · ICU vs NLS · PDF/A · text extraction · visual snapshot testing · copyleft (AGPL)

## After the video

1. Write the library-choice ADR for invoice rendering: options, licence review, platform notes, and the decision.
2. Un-ignore one renderer test by moving it to the new implementation, asserting on extracted text for an `en-CA` and an `fr-CA` invoice.
3. List every other `System.Drawing` or `CA1416` hit across the estate. It's rarely just one component.

## References

- `docs/brasswick-modernization-training-plan.md`: Section 6.2 (PDF row), Section 13 (`System.Drawing` row)
- `legacy/README.md`: "How this differs from the real thing"
- Video 18 (culture and encoding), video 19 (configuration inventory), video 24 (analyzers in CI)
- Microsoft Learn: *System.Drawing.Common only supported on Windows* (breaking change notice), *Globalization and ICU*, *Globalization invariant mode*
- PDFsharp 6, QuestPDF, SkiaSharp, PdfPig project documentation (licence pages in particular)
