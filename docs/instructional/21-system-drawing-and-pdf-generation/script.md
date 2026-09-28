# 21 · System.Drawing and PDF Generation

Welcome. Every .NET Framework estate has a Windows-only dependency that nobody owns. In FeeBilling, it's the invoicing project: PDF invoices drawn with `System.Drawing` and a Framework-only PDF library. It doesn't calculate money, but it produces the one document clients actually read. By the end of this lesson, you should be able to explain why `System.Drawing` doesn't move to Linux containers, find the bugs hiding in a renderer that "worked" for years, choose a replacement library the way a regulated vendor should, and test generated documents without comparing bytes.

## The questions this lesson answers

Here are the questions this lesson prepares you for. What do you do with Windows-only dependencies? How do you evaluate a third-party library for a regulated vendor? Why did this code work for years and fail now? And how do you test generated documents?

Here's the hook. Open `legacy/FeeBilling.Tests/Invoicing/InvoicePdfRendererTests.cs`. Every test in it is ignored, with the same reason: GDI+ not available on the build server. So nothing in CI has ever rendered an invoice. And one of the two seeded firms, Groupe Financier Laurentien, is a French-Canadian firm, with its culture recorded as `fr-CA`. Yet its invoices are formatted in whatever culture the server has, or, as we'll see, whatever culture the browser that clicked the link has.

## Why System.Drawing is Windows-only

Let's start from first principles. `System.Drawing` is a thin managed wrapper over GDI+, the graphics library built into Windows. Bitmaps, fonts, brushes and image encoding all call into native Windows code.

On Linux and macOS, the old Mono project filled the gap with a library called libgdiplus, which reimplemented GDI+ on top of other native libraries. It was never complete, it behaved differently in edge cases, and it had a history of bugs and security issues.

So Microsoft drew a line. Starting with .NET 6, the `System.Drawing.Common` package is supported only on Windows. On other platforms it throws at runtime. .NET 6 had a temporary switch to opt back in on Unix, and .NET 7 removed it. Microsoft's breaking-change notice spells out the details, so read it before you quote exact wording.

The compiler helps you find this. The platform compatibility analyzer, rule `CA1416`, flags calls to Windows-only APIs from code that claims to be cross-platform. That's one of three ways to find these dependencies early. The others are a simple `git grep` for `System.Drawing` across the estate, and actually running your code on Linux in CI, because a Linux build agent finds what reviews miss.

The Framework-only PDF library has the same problem, only worse. In the real product, it's a commercial library, version four, built for .NET Framework, and there is no newer version to upgrade to. In this repository, `legacy/README.md` explains the stand-in: PDFsharp version 1.50, the GDI+ build, referenced in `legacy/FeeBilling.Invoicing/FeeBilling.Invoicing.csproj`. It's the same shape of problem: Framework-only, and built on `System.Drawing`.

When you find a Windows-only dependency, you have three options. Replace it with a cross-platform library. Keep it on a Windows host for a while, as a separate service. Or run that component in a Windows container. For a leaf component like PDF generation, which has one interface and no shared state, replacement is usually right. If you do keep Windows for a while, give it a retirement date in the ADR, or "for now" becomes forever.

## The bugs IIS hid

Now open `legacy/FeeBilling.Invoicing/InvoicePdfRenderer.cs`. It's short, and it contains four real bugs. Each one was hidden by running on IIS on Windows, with a fixed server culture and low concurrency. Containers change all three.

Bug one is thread safety. Near the top, there's a comment: GDI+ objects cached for the lifetime of the app pool. Below it, a static field holds an `Image` called `_logo`, and a static lock object. The `GetLogo` method takes the lock, and creates the image once. That looks careful. But look at where the image is used. The render method calls `GetLogo().Save` to write the logo into a memory stream, and that call is outside the lock. So two concurrent requests use the same GDI+ image at the same time. GDI+ objects aren't thread-safe, and the classic symptom is an intermittent exception saying the object is currently in use elsewhere. The lock protected creation, not use. On a quiet IIS server, two invoices rarely render in the same instant. In a billing run producing forty thousand invoices, they will.

Bug two is file locking. When a logo path is configured, the code loads it with `Image.FromFile`. GDI+ keeps that file locked for as long as the image object lives, which here is the lifetime of the app pool. So nobody can replace the logo file without recycling the application.

Bug three is resource leaks. When no logo is configured, the code draws a placeholder with a new `SolidBrush` and a new `Font`, and never disposes either of them. GDI handles are a limited, per-process resource. Leaks like this don't show up in a test; they show up after weeks of uptime.

Bug four is culture, and it's the most interesting. Two lines draw the period ending and the amount due. The period end uses `ToShortDateString`, and the amount uses `ToString` with the "C" currency format, and both lines carry the comment "server culture". Now open `legacy/FeeBilling.Web/Web.config` and find the globalization element: culture is set to auto, and so is the UI culture. In ASP.NET, "auto" means the request thread's culture follows the browser's Accept-Language header. So the format of an invoice amount depends on who clicked the PDF link. A reviewer with a French browser and a reviewer with an English browser get differently formatted versions of the same invoice.

And the renderer's input can't fix it, because it doesn't carry the information. `InvoiceDocument.cs` has the firm name, account number, account name, household code, period end, amount and schedule code. There's no culture and no currency. Yet the database has both: the `Firms` table has a `Culture` column, defaulting to `en-CA`, and fee schedules have a `Currency` column, defaulting to CAD. The data exists; the renderer ignores it.

Two more things worth noticing. The seam already exists: `IInvoicePdfRenderer` is a one-method interface, and `UnityConfig.cs` registers the implementation in a single line, so swapping it is easy. And there's dead configuration: `Web.config` defines an `Invoice.FooterText` setting, but nothing in the code reads it. When you inventory configuration, inventory what's used, not just what exists.

## Choosing a replacement

Choosing a library at a regulated vendor isn't a matter of taste. It's a decision with legal and operational consequences, so the deliverable is an ADR, with the licence review attached.

Evaluate each candidate on six things. The licence model, including revenue thresholds and copyleft terms. Maintenance: how often it's released, and how many people maintain it. Whether it's pure managed code or depends on native assets. Its security history. Output requirements, such as PDF/A for long-term archiving. And performance at run scale, which for FeeBilling means tens of thousands of invoices per run.

Here are the realistic options. Licence terms change, so as of September 2026, check each one on the day you decide.

PDFsharp version six has a cross-platform build that doesn't depend on GDI+. It's closest to the legacy API, so it's the smallest code change. On Linux, it needs a font resolver, because it can't ask Windows for fonts, and some APIs changed from version 1.50.

QuestPDF has a fluent layout API that suits tabular documents like invoices. It uses a community licence below a revenue threshold and a commercial licence above it, so check the threshold against the company's revenue.

SkiaSharp is a wrapper over Google's Skia graphics library, with a PDF backend. It's permissively licensed, but it ships native assets, so the container image needs the right Linux native package.

ImageSharp is a managed imaging library, with a split licence that's commercial for some organizations. You only need it if you manipulate images. Embedding a PNG logo usually isn't that.

HTML to PDF through headless Chromium, for example driven by Playwright, lets designers edit templates as HTML. The costs are a large container image, slow cold starts, sandboxing concerns, and output that can drift between Chromium versions.

And watch out for copyleft. Some PDF libraries are offered under the AGPL. In a hosted software-as-a-service product, AGPL obligations are a legal review item, not an engineering choice.

## Redesign behind the seam

Keep the `IInvoicePdfRenderer` interface; it's a good seam. Change what goes through it and what lives behind it.

`InvoiceDocument` gains a culture and a currency, sourced from the firm's `Culture` column and the schedule's `Currency` column. The period end becomes a `DateOnly`, because it's a date, not a moment in time. Inside the renderer, you clone the culture's number format, set the currency symbol, and format the date and amount explicitly with that culture. The server's culture no longer matters, and neither does the browser's.

Branding becomes an immutable object, loaded once at startup, from configuration or Blob Storage: the logo as bytes, and the footer text. Because nothing mutates it, it's safe to register as a singleton and share across threads. Each render creates its own document, its own fonts and its own image from those bytes. No static mutable state, no `ConfigurationManager`, no server culture. The legacy image looked just as shareable, but it wasn't, because GDI+ objects carry mutable native state.

Look at the other side of the seam too. In `legacy/FeeBilling.Web/Controllers/Api/InvoicesController.cs`, the PDF action loads an invoice, then walks from the invoice to its account, from the account to its firm and household, and from there to a fee schedule and the billing run. Every one of those steps is a lazy-loaded navigation property, so a single PDF costs several database round trips. That's tolerable for one click. It's not tolerable when a billing run wants to render forty thousand invoices. In the new design, one projection query builds the whole `InvoiceDocument`, including the firm's culture and the schedule's currency, and rendering becomes a pure function of that document.

That purity pays off twice. It makes rendering easy to run in bulk from the billing worker, in parallel, without a web request in sight. And it makes testing trivial, because a test only needs to construct a document, not a database.

Roll the new renderer out the same way as everything else in this series. Render the seed invoices with both the legacy and the new renderer, extract the text from both, and compare. Every difference should be one you intended, such as French formatting for the French-Canadian firm. Then switch renderers per firm behind a flag, so if a client's finance team objects to a layout change, you can roll back one firm without touching the others. And if the regulated retention policy requires archived invoices to stay readable for years, make PDF/A output a requirement in the ADR, because not every library produces it.

## Fonts and globalization in containers

Two things bite when this code first runs in a Linux container.

Fonts. The legacy code asks for Arial. Linux images don't ship Arial, and Arial's licence may not allow you to add it. "It rendered on my machine" usually means Windows had the font. The fix is to embed licensed fonts through the library's font resolver, or install a font package in the image, and to check that the font's licence permits embedding in documents you distribute.

Globalization. In a container with no language environment variable set, the current culture is the invariant culture. Format an amount with "C" under the invariant culture, and instead of a dollar sign you get the generic currency sign, a small circle with four spikes. Some base images, such as Alpine and the chiseled images, default to invariant globalization mode with no ICU data at all, so asking for `fr-CA` may not even work. And on Windows, .NET uses ICU by default since .NET 5, but older Framework code used the Windows NLS data, and the French-Canadian thousands separator can differ between them. Verify on your base image. The lesson is the same as in lesson eighteen: culture is business data. Pass it in, and make sure the image has the data to honour it.

## Testing generated documents

Never compare PDF bytes. PDFs embed creation dates, document identifiers and producer version strings, so two renders of the same invoice differ even when nothing visible changed.

Instead, test at two levels. First, content: extract the text, for example with the PdfPig library, and assert on what matters, such as the account number, the period and the amount. A theory test can render the same invoice for `en-CA` and `fr-CA` and check that the amount appears as $5,312.50 in English and as five thousand three hundred twelve comma fifty, with a space separator and a trailing dollar sign, in French. Normalize the different space characters before comparing. You can also do text-level parity against legacy-rendered PDFs for the seed invoices.

Second, layout: render a page to an image and compare it to an approved snapshot, with a tolerance, so tiny anti-aliasing differences don't fail the build.

And add a concurrency test that renders fifty invoices in parallel. It would have caught bug one years ago.

## Traps

Leaving it on Windows "for now", without a date. One Windows host for one leaf component keeps Windows in the platform indefinitely.

Assuming a lock makes GDI+ safe. The lock guarded creation, not use. Better still, share nothing.

Server culture in client documents. Legacy formatting depended on who clicked. In a container, it depends on an environment variable nobody set.

Fonts that exist only on your machine.

Byte-comparing PDFs.

Licence drift. Several popular .NET libraries changed their licence terms in recent years. Record the licence review in the ADR and re-check it on major upgrades.

And dead configuration, like the footer text nobody reads.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What do you do with Windows-only dependencies?

[pause 5s]

Find them early: the CA1416 platform analyzer, a grep across the estate, and a Linux job in CI. Then isolate each one behind an interface. For each, decide whether to replace it, keep it on a Windows host temporarily with a retirement date, or run it in a Windows container. For a leaf component like PDF generation, with one interface and no shared state, replacement is usually right. In FeeBilling, the renderer interface already exists, so the swap is a one-line registration change.

**Interviewer:** How do you evaluate a third-party library for a regulated vendor?

[pause 5s]

Licence first: the model, revenue thresholds, and copyleft terms, because an AGPL library in a hosted product is a legal question. Then maintenance cadence and how many maintainers there are, whether it's pure managed code or needs native assets, its security history, output requirements such as PDF/A for archiving, and performance at our scale, which is tens of thousands of invoices per run. The result goes in an ADR with the licence review attached, and gets re-checked on major upgrades.

**Interviewer:** Why did this code work for years and fail now?

[pause 5s]

Because IIS on Windows hid its assumptions. GDI+ was present, the server culture was fixed, and concurrency was low, so a shared GDI+ image used outside its lock rarely collided. Containers remove GDI+, default to the invariant culture, and billing runs render thousands of invoices in parallel. The bugs were always there; the environment stopped covering for them.

**Interviewer:** How do you test generated documents?

[pause 5s]

Extract the text and assert on content, like the amount, period and account, for each culture we support. Use visual snapshots with a tolerance for layout. Add a concurrency test. And never compare bytes, because PDFs embed creation dates, document IDs and producer versions that change on every render.

## Recap

Five things to remember from this lesson.

One: `System.Drawing` wraps Windows GDI+, and since .NET 6 it's supported only on Windows. Find these dependencies early with the analyzer and a Linux CI job.

Two: the legacy renderer hid four bugs: a shared GDI+ image used outside its lock, a locked logo file, leaked handles, and invoice formatting that followed the browser's culture.

Three: choosing a replacement is a licence and operations decision, recorded in an ADR, not a preference.

Four: keep the seam, make culture and currency explicit inputs from the firm and schedule, and share only immutable data.

Five: test content and layout, never bytes.

In the next lesson, we'll move to the front end: migrating the AngularJS app to Angular with a route-level strangler, and why the gateway can't see hash URLs.
