# 18 · BinaryFormatter, Encoding and Culture

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work package:** WP-05 (parsing and staging) · **Prerequisites:** 04, 17

**Audio lesson:** [18-binaryformatter-encoding-and-culture.mp3](18-binaryformatter-encoding-and-culture.mp3) · [Transcript](script.md)

## Why this video exists

"What breaks *silently* when you move to modern .NET?" is the question that separates people who've done a migration from people who've read about one. The loud breaks (missing `System.Web`, no WCF server) stop the build. The silent ones compile, run, pass shallow tests, and corrupt data. FeeBilling's custodian ingestion has three of them in eight lines: `Encoding.Default` means something different on .NET Core, `decimal.Parse` depends on whichever culture the server (or the *browser*) has, and parsed batches are staged with `BinaryFormatter`, which no longer works at all on .NET 9+ and was a remote-code-execution risk before that. This video fixes all three using the byte-exact French-Canadian seed file, and lists the other silent breakers to name in an interview.

## Learning objectives

By the end, the viewer can:

- Explain why `BinaryFormatter` was removed, why it's dangerous, and migrate existing `varbinary` payloads without deserializing untrusted types.
- Explain what `Encoding.Default` returns on .NET Framework vs modern .NET, and read Windows-1252 files correctly on .NET 10.
- Parse numbers and dates from files independently of the current culture, including the fr-CA non-breaking-space group separator.
- Explain ICU vs NLS globalization, invariant globalization mode, and why culture data isn't a file-format specification.
- Replace `Substring` magic numbers with a fixed-width field spec, and replace "one bad line fails the batch" with per-line quarantine.
- Write the tests that prove it: the same file parses identically under any current culture, and the two seed files parse to identical positions.

## Interview questions this prepares you for

| Question | What a strong answer includes |
|---|---|
| What breaks silently when moving to modern .NET? | `Encoding.Default` becomes UTF-8. `BinaryFormatter` is gone. Globalization moves from NLS to ICU, so culture data changes. JSON casing and serializer behaviour. EF Core has no lazy loading by default and translates queries differently. `System.Drawing` is Windows-only. Distributed transactions need opt-in and Windows. Each with a concrete example. |
| Why is `BinaryFormatter` dangerous? | The payload names the types to instantiate, so a crafted payload can trigger gadget chains and execute code. There's no safe way to use it on untrusted input, and "it's our own database" isn't a trust boundary. Obsolete since .NET 5; the implementation was removed in .NET 9. |
| How do you migrate data serialized with `BinaryFormatter`? | Stop writing it first. Then convert existing rows once: either a `net472` tool that deserializes a known, closed set of types and re-serializes to JSON, or decode the NRBF stream on .NET 9+ with `System.Formats.Nrbf`, which reads records without instantiating types. Keep a format marker so both can coexist during the switch. |
| How do you parse numbers from files safely? | The file format defines the culture, not the server. Use an explicit `NumberFormatInfo` (or `CultureInfo.InvariantCulture`), `ParseExact` for dates, an explicit encoding, and `TryParse` with per-line reason codes. Test under several current cultures. |

## FeeBilling code on screen

| File | What to show |
|---|---|
| `legacy/FeeBilling.Ingestion.Wcf/CustodianFeedService.svc.cs` | `Encoding.Default`, `Substring` offsets, culture-dependent `decimal.Parse`/`DateTime.Parse`, `BinaryFormatter` write and read, "One bad line fails the whole batch" |
| `legacy/FeeBilling.Ingestion.Wcf/DataContracts.cs` | `[Serializable] Position`: "the assembly-qualified type name ... is baked into every row" |
| `database/billing/001-schema.sql` | `StagedBatches.Payload varbinary(max)`: "a BinaryFormatter-serialized List<Position>" |
| `seed/README.md`, `seed/custodian-files/NBIN_20260930_POS_FR.txt` | Windows-1252, CRLF, NBSP (`0xA0`) group separator, `Côté` and `Hélène` as single bytes |
| `legacy/FeeBilling.Tests/Ingestion/CustodianFeedServiceTests.cs` | The FB-274 fr-CA test (one of the 9 failing tests), and fixtures built with `Encoding.Default` on both ends |
| `legacy/FeeBilling.Ingestion.Wcf/FeedFileName.cs` | The contrast: `ParseExact` + `InvariantCulture` done right, next to a culture-sensitive `ToUpper()` |
| `legacy/FeeBilling.Web/Web.config` | `<globalization culture="auto" uiCulture="auto" />`: thread culture follows the browser |
| `legacy/FeeBilling.Core/FeeCalculator.cs`, `legacy/FeeBilling.Web/Helpers/CsvExporter.cs` | `ToShortDateString()` in a cache key; `value.ToString()` in CSV output |

## Run sheet

| Time | Segment | Content |
|---|---|---|
| 00:00–01:30 | Hook | Hex-dump the French seed file: `43 F4 74 E9` for `Côté`, and `A0` between the digit groups. Same file, three runtimes/cultures, three different results, and no exception in two of them. |
| 01:30–05:30 | `BinaryFormatter` | Timeline: obsolete in .NET 5, errors by default in more project types over later releases, implementation removed in .NET 9 (it now always throws `PlatformNotSupportedException`). Why it's an RCE risk, and why `ProcessPendingBatches` "trusts whatever is in the column". The type name baked into every row. Migration options: a `net472` conversion tool, or `NrbfDecoder`. Better still, stage the **raw file** (video 17), not your parse of it. |
| 05:30–09:00 | `Encoding.Default` | Framework: the machine's ANSI code page (Windows-1252 on a typical Western server, but machine-dependent). .NET Core and later: UTF-8. Live: decode the French file on .NET 10 and watch `Côté` become `C?t?` *and* the NBSP in `609 471,47` become U+FFFD, so the number no longer parses in any culture. Fix: `CodePagesEncodingProvider` and an explicit `GetEncoding(1252)`. |
| 09:00–13:00 | Culture | `decimal.Parse` uses the current culture. The invariant file breaks on a fr-CA server (the FB-274 test); the French file breaks on an en-CA server. `culture="auto"` means the current culture of the web app is chosen by each user's browser. ICU vs NLS: on this machine .NET 10 reports U+00A0 as the fr-CA group separator but U+202F for fr-FR; culture data changes between ICU versions. Invariant globalization in containers. |
| 13:00–15:30 | A robust parser | A fixed-width field spec from the 2014 layout, an explicit encoding and `NumberFormatInfo` chosen per source, `DateOnly.TryParseExact`, CRLF handling, and per-line quarantine with reason codes. |
| 15:30–17:30 | Other silent breakers | `"nbin".ToUpper()` under tr-TR is `NBİN` (so `FeedFileName` isn't quite right either). `ToShortDateString()` cache keys. CSV output that changes with culture. `ToString("C")` with invariant culture prints `¤`. Symmetric test fixtures (`Encoding.Default` on both ends) hide encoding bugs. |
| 17:30–19:00 | Tests and a CI ban | The two acceptance tests below. A CI check that fails on any `BinaryFormatter` reference, or on the unsupported compatibility package, in `src/`. |
| 19:00–20:00 | Recap | The three lines to remember: stage raw bytes, decode explicitly, parse by specification. |

### Before

```csharp
var lines = Encoding.Default.GetString(content).Split('\n');   // Encoding.Default differs on .NET Core
var positions = lines.Skip(1).Select(l => new Position
{
    AccountNumber = l.Substring(0, 12).Trim(),
    MarketValue   = decimal.Parse(l.Substring(40, 18)),        // culture-dependent; fr-CA servers break
    AsOfDate      = DateTime.Parse(l.Substring(58, 10))        // same
}).ToList();

var formatter = new BinaryFormatter();                         // removed in .NET 9
```

| Runtime and current culture | Invariant file (`1052590.68`) | French file (`1 052 590,68`) |
|---|---|---|
| .NET Framework, en-CA server | Parses | `FormatException` (NBSP isn't a group separator in en-CA) |
| .NET Framework, fr-CA server (the Montreal data centre, FB-274) | `FormatException` (`.` isn't the decimal separator) | Parses |
| .NET 10, any culture | Parses only where `.` is the decimal separator | Fails everywhere: UTF-8 decoding has already turned `0xA0` into U+FFFD |

### After: parsing by specification

```csharp
// Registered once at startup. On .NET 10 the provider ships with the runtime; netstandard2.0 libraries
// need the System.Text.Encoding.CodePages package.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

public static class Nbin2014Layout   // "Fixed-width layout (all custodians, per the 2014 spec)"
{
    public static readonly Field AccountNumber = new(0, 12);
    public static readonly Field AccountName   = new(12, 28);
    public static readonly Field MarketValue   = new(40, 18);   // right-aligned
    public static readonly Field AsOfDate      = new(58, 10);   // yyyy-MM-dd
    public const int LineLength = 68;
}

public readonly record struct Field(int Start, int Length)
{
    public ReadOnlySpan<char> Slice(string line) => line.AsSpan(Start, Length);
}

public sealed class PositionFileParser(Encoding encoding, NumberFormatInfo numbers)
{
    public static readonly NumberFormatInfo FrenchCanadianFile = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",   // from the file spec, not from whatever ICU says fr-CA uses today
    };

    public List<LineResult> Parse(byte[] content)
    {
        var results = new List<LineResult>();
        var lines = encoding.GetString(content).Split("\r\n");   // CRLF, no trailing newline, first line is a header

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length != Nbin2014Layout.LineLength)
            {
                results.Add(LineResult.Quarantined(i, "BAD_LENGTH"));
            }
            else if (!decimal.TryParse(Nbin2014Layout.MarketValue.Slice(line), NumberStyles.Number, numbers, out var value))
            {
                results.Add(LineResult.Quarantined(i, "INVALID_AMOUNT"));
            }
            else if (!DateOnly.TryParseExact(Nbin2014Layout.AsOfDate.Slice(line), "yyyy-MM-dd",
                         CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
            {
                results.Add(LineResult.Quarantined(i, "INVALID_DATE"));
            }
            else
            {
                results.Add(LineResult.Parsed(i,
                    Nbin2014Layout.AccountNumber.Slice(line).Trim().ToString(),
                    Nbin2014Layout.AccountName.Slice(line).Trim().ToString(),
                    value, asOf));
            }
        }

        return results;
    }
}

// Usage: the format comes from configuration for the source, never from the server's culture.
var parser = new PositionFileParser(Encoding.GetEncoding(1252), PositionFileParser.FrenchCanadianFile);
```

Note that `FeedFileName.Parse` splits `NBIN_20260930_POS_FR` into four parts and keeps only the first three, so the `_FR` marker is silently dropped. Don't rely on it to pick the format until that's fixed.

### After: migrating staged `BinaryFormatter` rows without deserializing them

`System.Formats.Nrbf` (a NuGet package, `10.0.0` for .NET 10) reads the NRBF stream as records and never loads or instantiates the types it names:

```csharp
using System.Formats.Nrbf;

static List<StagedPosition> ReadLegacyPayload(byte[] payload)
{
    ClassRecord list = NrbfDecoder.DecodeClassRecord(new MemoryStream(payload));   // List<Position>
    var size = list.GetInt32("_size");
    var items = (SZArrayRecord<SerializationRecord>)list.GetArrayRecord("_items")!;

    return items.GetArray()
        .Take(size)                                   // slots past _size are unused capacity
        .Cast<ClassRecord>()
        .Select(p => new StagedPosition(
            p.GetString("<AccountNumber>k__BackingField")!,   // BinaryFormatter stores fields: auto-property backing fields
            p.GetDecimal("<MarketValue>k__BackingField"),
            DateOnly.FromDateTime(p.GetDateTime("<AsOfDate>k__BackingField"))))
        .ToList();
}
```

Verify the record shapes against a real payload (generate one from a `net472` run of the legacy service) before recording. The alternative in the training plan, a one-time `net472` tool, is also valid: it can deserialize, but only with a serialization binder that allows exactly `List<Position>` and `Position`.

## Demo

```bash
# The bytes: ô = F4, é = E9, NBSP = A0, CRLF line endings
od -c seed/custodian-files/NBIN_20260930_POS_FR.txt | head -20
# PowerShell alternative: Format-Hex seed/custodian-files/NBIN_20260930_POS_FR.txt | Select-Object -First 20
```

Save this outside the repo (the repo's `Directory.Build.props` and central package management would otherwise apply to it) and run it with `dotnet run encoding.cs`, passing the path to the repo:

```csharp
using System.Globalization;
using System.Text;

var file = Path.Combine(args[0], "seed", "custodian-files", "NBIN_20260930_POS_FR.txt");
var bytes = File.ReadAllBytes(file);

Console.WriteLine(Encoding.Default.WebName);                          // utf-8 on .NET 10
Console.WriteLine(Encoding.Default.GetString(bytes).Split('\n')[3]);  // C�t�, Jean - REER ... 609�471,47

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var line = Encoding.GetEncoding(1252).GetString(bytes).Split("\r\n")[3];
Console.WriteLine(line);                                              // Côté, Jean - REER ... 609 471,47

var fr = CultureInfo.GetCultureInfo("fr-CA");
Console.WriteLine($"fr-CA group separator: U+{(int)fr.NumberFormat.NumberGroupSeparator[0]:X4}");
Console.WriteLine($"fr-FR group separator: U+{(int)CultureInfo.GetCultureInfo("fr-FR").NumberFormat.NumberGroupSeparator[0]:X4}");
Console.WriteLine("nbin".ToUpper(CultureInfo.GetCultureInfo("tr-TR")));   // NBİN
```

Then show the acceptance tests (WP-05):

```csharp
[Theory]
[InlineData("en-CA")]
[InlineData("fr-CA")]
[InlineData("tr-TR")]
public void SeedFiles_ParseToIdenticalPositions_UnderAnyCurrentCulture(string culture)
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);   // the parser must not care

        var windows1252 = Encoding.GetEncoding(1252);   // CodePagesEncodingProvider registered once in the test assembly
        var invariant = new PositionFileParser(windows1252, NumberFormatInfo.InvariantInfo).Parse(Seed("NBIN_20260930_POS.txt"));
        var french = new PositionFileParser(windows1252, PositionFileParser.FrenchCanadianFile).Parse(Seed("NBIN_20260930_POS_FR.txt"));

        Assert.Equal(12, french.Count(r => r.IsParsed));
        Assert.Equal(invariant, french);
        Assert.Contains(french, r => r.AccountName == "Côté, Jean - REER" && r.MarketValue == 609471.47m);
    }
    finally
    {
        CultureInfo.CurrentCulture = original;
    }
}
```

The fixtures are the byte-exact files under `seed/custodian-files/` (marked `binary` in `.gitattributes`), not strings encoded in the test with the same encoding the parser uses.

## Traps to call out

- **Symmetric fixtures.** The legacy test builds its file with `Encoding.Default.GetBytes(...)` and the service decodes with `Encoding.Default.GetString(...)`. Both ends change together on .NET Core, so the test can't see the bug. Use real bytes.
- **"The column is ours, so `BinaryFormatter` is safe."** Anyone who can write to the database can then run code in the ingestion process. And on .NET 9+ it throws regardless. The unsupported compatibility package brings the risk back; ban it.
- **Trusting culture data as a format.** fr-CA and fr-FR already disagree on the group separator on the same machine, and ICU data changes between versions. A file format needs an explicit `NumberFormatInfo`.
- **Invariant globalization in containers.** Small container images often run with `InvariantGlobalization`, where culture-specific behaviour disappears. Depending on runtime settings, creating a culture like fr-CA can even throw (verify for your image). Code that parses by specification doesn't care.
- **`culture="auto"` on the server.** In the legacy web app, the same request can format numbers and dates differently depending on who sent it. Look for it before assuming "the server culture".
- **Splitting on `'\n'` in CRLF files.** Every line keeps a trailing `'\r'`. Here the offsets happen to stop before it, which is luck rather than design.
- **Failing the whole batch for one line.** Quarantine each bad line with a reason code and line number, alert, and process the rest. But agree with the business whether a partially accepted file is billable.

## Key terms

`BinaryFormatter` · NRBF · gadget chain · serialization binder · `System.Formats.Nrbf` · ANSI code page · Windows-1252 · `CodePagesEncodingProvider` · UTF-8 replacement character (U+FFFD) · NBSP (U+00A0) · narrow NBSP (U+202F) · ICU vs NLS · invariant globalization · `NumberFormatInfo` · fixed-width field spec · quarantine

## After the video

1. Implement `PositionFileParser` and the culture-independence tests against both seed files.
2. Write the "things that broke silently" list (the Day 7 interview artifact): each item with the FeeBilling file, the symptom, and the fix.
3. Add a CI step that fails on `BinaryFormatter` in `src/` (a `git grep` is enough to start; video 24 turns it into an analyzer).

## References

- `seed/README.md`, `docs/brasswick-modernization-training-plan.md`: Section 9 WP-05, Section 10 (S12), Section 12.2 ("What breaks silently")
- Microsoft Learn: *BinaryFormatter migration guide*, *Read BinaryFormatter (NRBF) payloads with System.Formats.Nrbf*, *Globalization and ICU*, *Globalization invariant mode*, *CodePagesEncodingProvider*
