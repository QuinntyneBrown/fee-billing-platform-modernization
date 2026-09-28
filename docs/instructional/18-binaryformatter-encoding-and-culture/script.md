# 18 · BinaryFormatter, Encoding and Culture

Welcome. This lesson answers the question that separates people who've done a migration from people who've read about one: what breaks silently when you move to modern .NET? The loud breaks, like a missing `System.Web` or no WCF server, stop the build, so you can't miss them. The silent ones compile, run, pass shallow tests, and corrupt data. FeeBilling's custodian ingestion has three of them in eight lines of code. By the end of this lesson, you should be able to explain all three, fix them, prove the fix with tests, and name the other silent breakers an interviewer will expect.

## The questions this lesson answers

Here's what you'll be ready for. What breaks silently when moving to modern .NET? Why is BinaryFormatter dangerous? How do you migrate data that was serialized with BinaryFormatter? And how do you parse numbers from files safely?

The running example is the French-Canadian seed file in `seed/custodian-files`. It's a byte-exact fixture, marked as binary in `.gitattributes` so git never touches it. The same bytes give three different results depending on the runtime and the culture, and in two of those cases, there's no exception at all.

## Eight lines of ingestion code

Open `legacy/FeeBilling.Ingestion.Wcf/CustodianFeedService.svc.cs` and find `SubmitPositionFile`. It does four things.

First, it turns the file's bytes into text with `Encoding.Default.GetString`, and splits the text on the newline character. The comment says `Encoding.Default` differs on .NET Core.

Second, it skips the header line and cuts each line into fields with `Substring` at fixed offsets: the account number from position zero for twelve characters, the market value from position forty for eighteen, and the as-of date from position fifty-eight for ten.

Third, it parses the market value with `decimal.Parse` and the date with `DateTime.Parse`, with no culture argument. The comment says: culture-dependent, fr-CA servers break.

Fourth, it serializes the list of positions with `BinaryFormatter` into a byte array, and saves it in the `StagedBatches` table. The comment says BinaryFormatter was removed in .NET 9.

Later, `ProcessPendingBatches` reads each staged payload back, deserializes it with BinaryFormatter, with a comment that says it trusts whatever is in the column, and upserts the positions. If anything throws, the whole batch is marked failed. The comment: one bad line fails the whole batch.

Let's take the three problems in turn.

## BinaryFormatter

BinaryFormatter is .NET's original binary serializer. It writes a format called NRBF, and the important thing about that format is that the payload names the types to create. When you deserialize, BinaryFormatter reads a type name from the bytes and instantiates it.

That's why it's dangerous. If an attacker can control the bytes, they choose which types get created and in what state. Security researchers have found chains of ordinary framework types, called gadget chains, whose constructors and callbacks, when combined, execute arbitrary code. So deserializing untrusted BinaryFormatter data is a remote code execution vulnerability, and there's no configuration that makes it safe on untrusted input.

People push back with: but it's our own database. That's not a trust boundary. Anyone who can write to the `StagedBatches` table, through SQL injection somewhere else, a compromised account, or a misconfigured tool, can then run code inside the ingestion process.

The timeline is worth knowing. BinaryFormatter was marked obsolete in .NET 5, it became an error by default in more project types over later releases, and in .NET 9 the implementation was removed. Calling it now always throws `PlatformNotSupportedException`. There's an unsupported compatibility package that brings it back. Ban that too.

There's another subtle problem. Open `legacy/FeeBilling.Ingestion.Wcf/DataContracts.cs`. The `Position` class is marked serializable, and its comment says the assembly-qualified type name, FeeBilling dot Ingestion dot Wcf dot Position, is baked into every row. So the staged data is coupled to an assembly name and a namespace that the migration is about to change.

How do you migrate the existing rows? Start by stopping new writes. The best fix is to stage the raw file, not your parse of it. That's what the CoreWCF host from the last lesson does: it writes the original bytes to Blob Storage. Then there's nothing to deserialize.

For the rows already in the table, there are two options. The training plan's option is a one-time tool built for .NET Framework 4.7.2, which can still run BinaryFormatter. It reads each row, deserializes it, and re-serializes it to JSON. If you do that, use a serialization binder that allows exactly two types, the list of positions and the position itself, and rejects everything else. The second option is newer. .NET 9 introduced a package called `System.Formats.Nrbf`, with a class called `NrbfDecoder`. It reads an NRBF stream as a tree of records, class records and array records with named fields, without ever loading or instantiating the types they name. So you can read the list's internal items array and size, and each position's fields, on .NET 10, safely. Verify the record shapes against a real payload before you depend on them. Either way, keep a format marker on each row, so old and new formats can coexist during the switch.

## Encoding.Default

The second silent breaker is one word: Default.

On .NET Framework, `Encoding.Default` returns the machine's ANSI code page. On a typical Western Windows server, that's Windows-1252, but it's machine-dependent. On .NET Core and every modern .NET, `Encoding.Default` is always UTF-8.

Now look at the seed file. `seed/README.md` says the position files are encoded in Windows-1252, because the feed agent runs on French Windows. So accented names are single bytes. In the name Côté, the o with a circumflex is the byte hex F4, and the e with an acute accent is hex E9. The French file also formats numbers the French-Canadian way, with a comma before the cents and a non-breaking space, hex A0, between the groups of thousands. One line shows an account for Côté, Jean, with a market value of $609,471.47, written with that non-breaking space and a comma.

Decode those bytes as UTF-8 on .NET 10 and here's what happens. Hex F4 and hex E9 aren't valid on their own in UTF-8, so each becomes the Unicode replacement character, and Côté prints as C, question mark, t, question mark. That's bad enough for client names on invoices. But the non-breaking space is also a single invalid byte, so it also becomes the replacement character. Now the market value contains a character that isn't a digit or a separator in any culture, and it won't parse anywhere. The encoding bug has turned into a number bug.

The fix is to never use the default. On .NET 10, call `Encoding.RegisterProvider` with `CodePagesEncodingProvider.Instance` once at startup, and then call `Encoding.GetEncoding` with 1252, explicitly. The code-page provider ships with the modern runtime. A .NET Standard library would need the `System.Text.Encoding.CodePages` package. And the encoding should come from the configuration for each custodian source, because it's part of the file format.

## Culture

The third silent breaker is culture. `decimal.Parse` and `DateTime.Parse` without a format provider use the current culture of the thread.

Think through the combinations. On .NET Framework with an English-Canadian server, the invariant file, with a decimal point, parses. The French file doesn't, because the non-breaking space isn't a group separator in English. On a French-Canadian server, it's the other way round: the invariant file fails, because a period isn't the decimal separator in French. That second case is a real ticket. Open `legacy/FeeBilling.Tests/Ingestion/CustodianFeedServiceTests.cs`. There's a test labelled FB-274, with the comment that the Montreal data centre servers run fr-CA. It sets the thread culture to French-Canadian and submits values with a decimal point. It's one of the nine failing tests in the legacy suite. And on .NET 10, the French file fails in every culture, because the encoding already destroyed it.

It gets worse in the web app. Open `legacy/FeeBilling.Web/Web.config` and find the globalization element: culture and UI culture are both set to auto, with the comment that the thread culture follows the browser's Accept-Language header. So the current culture of the web application isn't the server's culture. It's chosen by each user's browser. Any culture-sensitive parsing or formatting in a request can behave differently for different users.

Then there's the platform change. On Windows, .NET Framework gets culture data from the operating system, called NLS. Since .NET 5, modern .NET uses ICU, the International Components for Unicode library, on Windows too. Culture data can differ between them, and between ICU versions. On a .NET 10 machine, the French-Canadian group separator is a plain non-breaking space, but the French-France group separator is a narrow non-breaking space, a different character. So "use the French culture to parse French files" isn't even a stable rule.

And in containers, small images often run in invariant globalization mode, where culture-specific data is absent. Depending on the settings, creating a culture like French-Canadian may even throw, so check that for your image.

The lesson is: culture data isn't a file format specification. The file format defines how numbers are written, not the server, and not the user's browser.

## A parser that follows the specification

So here's the robust version, and it's the answer to "how do you parse numbers from files safely?"

First, a fixed-width field spec instead of magic numbers. The comment in the legacy service describes the 2014 layout: account number at zero for twelve, account name at twelve for twenty-eight, market value at forty for eighteen, right-aligned, and the as-of date at fifty-eight for ten, in year-month-day format. Put that in a small layout class with named fields and a line length of sixty-eight, and slice each line by field.

Second, an explicit encoding and an explicit `NumberFormatInfo`, chosen per source from configuration. For the French file, create a number format with a comma as the decimal separator and a non-breaking space as the group separator, taken from the file spec, not from whatever ICU says French-Canadian uses today. For the invariant file, use `NumberFormatInfo.InvariantInfo`.

Third, exact parsing. `decimal.TryParse` with the number style and that format. `DateOnly.TryParseExact` with the year-month-day format and the invariant culture, because the as-of date has no time.

Fourth, handle line endings. The files use carriage return and line feed, with no trailing newline, and the first line is a header. The legacy code splits on the line feed only, so every line keeps a trailing carriage return. The offsets happen to stop before it, which is luck, not design. Split on the full sequence.

And fifth, quarantine instead of failing the batch. When a line has the wrong length, an invalid amount or an invalid date, record it with its line number and a reason code, alert on it, and process the rest. Agree with the business whether a partially accepted file is billable, because that's a business decision.

Legacy has one piece that does this right, and it makes a good contrast. `legacy/FeeBilling.Ingestion.Wcf/FeedFileName.cs` parses the file name's date with `DateTime.ParseExact`, a fixed format and the invariant culture. But even it isn't perfect. It upper-cases the custodian code with `ToUpper`, which is culture-sensitive. Under the Turkish culture, lower-case i upper-cases to a capital I with a dot, so NBIN comes out wrong. Use `ToUpperInvariant`. And it splits the file name on underscores and keeps only the first three parts, so the FR marker on the French file is silently dropped. Don't rely on it to pick the format until that's fixed.

## Other silent breakers, and the tests

Round out the list with a few more from this repository. The fee calculator builds its cache key with `ToShortDateString`, which is culture-dependent. The CSV exporter in `legacy/FeeBilling.Web/Helpers/CsvExporter.cs` formats values with `ToString`, using the server's current culture. And formatting currency with the C format under the invariant culture prints the generic currency sign, not a dollar sign. Beyond this lesson, the interview list also includes JSON casing and serializer behaviour, EF Core's lack of lazy loading and different query translation, `System.Drawing` being Windows-only, and distributed transactions needing an opt-in and Windows.

Now, how do you prove the fix? Two acceptance tests from the work package. The first is a theory that runs under several current cultures, English-Canadian, French-Canadian and Turkish, and asserts the parser gives the same answer in each. The parser must not care. The second parses both seed files, the invariant one and the French one, and asserts they produce identical positions, twelve parsed lines, including Côté, Jean, with a market value of $609,471.47.

And there's a trap in the legacy test that's worth saying out loud. The legacy test builds its input with `Encoding.Default.GetBytes`, and the service decodes with `Encoding.Default.GetString`. Both ends change together on .NET Core, so the test can't see the encoding bug. That's called a symmetric fixture. Use the real, byte-exact files as fixtures instead.

Finally, a guardrail. Add a CI step that fails on any reference to BinaryFormatter, or the compatibility package, anywhere in `src`. A `git grep` is enough to start. Lesson twenty-four turns it into an analyzer.

## Traps

A few traps to call out.

Symmetric fixtures, which hide encoding bugs because both ends use the same default.

Believing BinaryFormatter is safe because the column is yours. And bringing it back with the compatibility package.

Trusting culture data as a file format. French-Canadian and French-France already disagree on one machine.

Forgetting invariant globalization in containers.

Missing the auto culture setting in `Web.config`, which lets each browser choose the culture.

Splitting on line feed in files with carriage-return line-feed endings.

And failing a whole batch for one bad line.

## Interview drill

Let's practise. After each question there's a short pause. Pause the audio if you want more time, answer out loud, and then compare your answer with the model answer.

**Interviewer:** What breaks silently when moving to modern .NET?

[pause 5s]

`Encoding.Default` changes from the machine's ANSI code page to UTF-8, so Windows-1252 files garble. We saw accented Québec client names and even numbers break. BinaryFormatter is gone in .NET 9. Globalization moves from NLS to ICU, so culture data changes. JSON serialization defaults change, including casing. EF Core has no lazy loading by default and translates queries differently. `System.Drawing` is Windows-only, and distributed transactions need Windows and an explicit opt-in.

**Interviewer:** Why is BinaryFormatter dangerous?

[pause 5s]

The payload names the types to instantiate, so a crafted payload can trigger gadget chains and execute code. There's no safe way to use it on untrusted input, and your own database isn't a trust boundary, because anyone who can write to it can then run code in your process. It was obsolete from .NET 5, and the implementation was removed in .NET 9, so it throws now anyway.

**Interviewer:** How do you migrate data that was serialized with BinaryFormatter?

[pause 5s]

First, stop writing it. Ideally stage the raw input rather than a serialized object. Then convert the existing rows once: either a .NET Framework tool that deserializes with a binder allowing only the known types and re-serializes to JSON, or, on .NET 9 and later, the `System.Formats.Nrbf` decoder, which reads the records without instantiating any types. Keep a format marker so both formats can coexist during the switch, and ban BinaryFormatter in CI.

**Interviewer:** How do you parse numbers from files safely?

[pause 5s]

The file format defines the culture, not the server. I use an explicit encoding and an explicit `NumberFormatInfo` per source, `TryParseExact` for dates, and a fixed-width field spec instead of magic offsets. Bad lines are quarantined with reason codes instead of failing the batch. And I test it under several current cultures, with the real byte-exact files as fixtures.

## Recap

Five things to remember from this lesson.

One: BinaryFormatter is a remote code execution risk and is removed in .NET 9. Stage raw bytes instead, convert old rows with a restricted binder or the NRBF decoder, and ban it in CI.

Two: `Encoding.Default` is the ANSI code page on .NET Framework and UTF-8 on modern .NET. Register the code-page provider and ask for Windows-1252 explicitly.

Three: parsing without a format provider uses the current culture, which in the legacy web app is chosen by the browser. Culture data changes between NLS and ICU, and between cultures you'd expect to agree.

Four: parse by specification: a field layout, an explicit number format and encoding per source, exact date parsing, correct line endings, and per-line quarantine.

Five: prove it with real byte-exact fixtures and tests under several cultures, because symmetric fixtures hide exactly these bugs.

In the next lesson, we'll replace Web.config, config transforms and log4net with options, structured logging and OpenTelemetry.
