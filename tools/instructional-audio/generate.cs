#:property PublishAot=false

// Builds the audio lessons in docs/instructional from each lesson folder's script.md, using
// Azure AI Speech neural text to speech (REST API + SSML).
//
//   dotnet run tools/instructional-audio/generate.cs -- --dry-run               validate every script, no network
//   dotnet run tools/instructional-audio/generate.cs -- 01 02                   synthesize folders whose name contains 01 or 02
//   dotnet run tools/instructional-audio/generate.cs -- --pronunciation-test    every lexicon term, to .cache/pronunciation-test.mp3
//
// Synthesis reads AZURE_SPEECH_KEY (never stored in the repo) and AZURE_SPEECH_REGION (default eastus2).
// Each ## section is one request (the service caps a request at 10 minutes of audio). Chunks are cached by
// SSML hash in tools/instructional-audio/.cache, so a re-run only pays for sections that changed.

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string NarratorVoice = "en-US-AndrewMultilingualNeural";
const string InterviewerVoice = "en-US-AvaMultilingualNeural";
const string OutputFormat = "audio-24khz-48kbitrate-mono-mp3";
const string Album = "FeeBilling Modernization: Interview Prep";
const double DollarsPerMillionCharacters = 15;

var dryRun = args.Contains("--dry-run");
var pronunciationTest = args.Contains("--pronunciation-test");
var filters = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var repoRoot = FindRepoRoot();
var lessonsRoot = Path.Combine(repoRoot, "docs", "instructional");
var toolDirectory = Path.Combine(repoRoot, "tools", "instructional-audio");
var cacheDirectory = Path.Combine(toolDirectory, ".cache");
Directory.CreateDirectory(cacheDirectory);

var lexicon = Lexicon.Load(Path.Combine(toolDirectory, "pronunciations.json"));
var builder = new ScriptBuilder(lexicon, NarratorVoice, InterviewerVoice);

var allFolders = Directory.GetDirectories(lessonsRoot)
    .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^\d{2}-"))
    .OrderBy(d => d, StringComparer.Ordinal)
    .ToList();

var lessons = new List<Lesson>();
var errors = new List<string>();

if (pronunciationTest)
{
    lessons.Add(builder.BuildPronunciationTest(Path.Combine(cacheDirectory, "pronunciation-test.mp3")));
}
else
{
    var selected = filters.Length == 0
        ? allFolders
        : allFolders.Where(f => filters.Any(x => Path.GetFileName(f).Contains(x, StringComparison.OrdinalIgnoreCase))).ToList();

    foreach (var folder in selected)
    {
        var name = Path.GetFileName(folder);
        var script = Path.Combine(folder, "script.md");
        if (!File.Exists(script))
        {
            Console.WriteLine($"skip  {name}: no script.md");
            continue;
        }

        var number = int.Parse(name[..2], CultureInfo.InvariantCulture);
        var lesson = builder.Parse(script, number, allFolders.Count, Path.Combine(folder, name + ".mp3"), errors);
        if (lesson is not null)
        {
            lessons.Add(lesson);
        }
    }
}

if (errors.Count > 0)
{
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }

    Console.Error.WriteLine($"{errors.Count} script error(s). Nothing synthesized.");
    return 1;
}

// Keep the SSML next to the cache so a request can be inspected or replayed.
foreach (var lesson in lessons)
{
    var ssmlDirectory = Path.Combine(cacheDirectory, "ssml", Path.GetFileNameWithoutExtension(lesson.OutputPath));
    Directory.CreateDirectory(ssmlDirectory);
    for (var i = 0; i < lesson.Chunks.Count; i++)
    {
        File.WriteAllText(Path.Combine(ssmlDirectory, $"{i:00}.xml"), lesson.Chunks[i].Ssml);
    }
}

PrintSummary(lessons);

if (dryRun)
{
    return 0;
}

var keyVariable = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
if (string.IsNullOrWhiteSpace(keyVariable))
{
    Console.Error.WriteLine("AZURE_SPEECH_KEY is not set.");
    return 1;
}

string key = keyVariable;

var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "eastus2";
var endpoint = new Uri($"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

var pending = lessons
    .SelectMany(l => l.Chunks)
    .Where(c => !File.Exists(CachePath(c)))
    .DistinctBy(c => c.Hash)
    .ToList();

Console.WriteLine($"Synthesizing {pending.Count} chunk(s) ({pending.Sum(c => c.Characters):N0} characters); the rest are cached.");

await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (chunk, ct) =>
{
    var audio = await SynthesizeAsync(http, endpoint, key, chunk.Ssml, ct);
    var temporary = CachePath(chunk) + ".tmp";
    await File.WriteAllBytesAsync(temporary, audio, ct);
    File.Move(temporary, CachePath(chunk), overwrite: true);
    Console.WriteLine($"  done  {chunk.Label}");
});

foreach (var lesson in lessons)
{
    using var frames = new MemoryStream();
    var seconds = 0.0;
    var timeline = new List<(Chunk Chunk, double Start, double Duration)>();
    foreach (var chunk in lesson.Chunks)
    {
        var chunkFrames = Mp3.ExtractFrames(File.ReadAllBytes(CachePath(chunk)), out var chunkSeconds);
        if (chunkSeconds > 9.9 * 60)
        {
            Console.WriteLine($"  WARNING {chunk.Label} is {chunkSeconds / 60:0.0} min; the service truncates at 10 min.");
        }

        frames.Write(chunkFrames);
        timeline.Add((chunk, seconds, chunkSeconds));
        seconds += chunkSeconds;
    }

    // Where each paragraph falls in the MP3, for tools that sync slides or captions to the narration
    // (tools/instructional-video). Chunk boundaries are exact; paragraph times within a chunk are estimated.
    var timingsDirectory = Path.Combine(cacheDirectory, "timings");
    Directory.CreateDirectory(timingsDirectory);
    Timings.Write(Path.Combine(timingsDirectory, Path.GetFileNameWithoutExtension(lesson.OutputPath) + ".json"), seconds, timeline);

    var tag = Mp3.BuildId3Tag(
    [
        ("TIT2", lesson.TagTitle),
        ("TALB", Album),
        ("TPE1", "FeeBilling Modernization"),
        ("TRCK", lesson.TrackNumber),
        ("TCON", "Speech"),
        ("TYER", "2026"),
    ]);

    await using (var output = File.Create(lesson.OutputPath))
    {
        output.Write(tag);
        frames.Position = 0;
        await frames.CopyToAsync(output);
    }

    Console.WriteLine($"wrote {Path.GetRelativePath(repoRoot, lesson.OutputPath)}  {TimeSpan.FromSeconds(seconds):mm\\:ss}  {new FileInfo(lesson.OutputPath).Length / 1_048_576.0:0.0} MB");
}

return 0;

string CachePath(Chunk chunk) => Path.Combine(cacheDirectory, chunk.Hash + ".mp3");

static void PrintSummary(List<Lesson> lessons)
{
    Console.WriteLine($"{"lesson",-50} {"chunks",6} {"words",6} {"est min",7} {"max chunk",9} {"voices",6} {"chars",8}");
    foreach (var lesson in lessons)
    {
        var words = lesson.Chunks.Sum(c => c.Words);
        Console.WriteLine(
            $"{Path.GetFileNameWithoutExtension(lesson.OutputPath),-50} {lesson.Chunks.Count,6} {words,6} {words / ScriptBuilder.WordsPerMinute,7:0.0} " +
            $"{lesson.Chunks.Max(c => c.Words) / ScriptBuilder.WordsPerMinute,9:0.0} {lesson.Chunks.Max(c => c.VoiceTags),6} {lesson.Chunks.Sum(c => c.Characters),8:N0}");
        if (words is < 2500 or > 3600)
        {
            Console.WriteLine($"  WARNING {words} words is outside the 2,500-3,600 target (~17-24 min).");
        }
    }

    var characters = lessons.Sum(l => l.Chunks.Sum(c => c.Characters));
    Console.WriteLine($"Total: {lessons.Count} lesson(s), {characters:N0} billable characters (estimate), about ${characters / 1_000_000.0 * DollarsPerMillionCharacters:0.00}.");
}

static async Task<byte[]> SynthesizeAsync(HttpClient http, Uri endpoint, string key, string ssml, CancellationToken ct)
{
    const int maxAttempts = 6;
    for (var attempt = 1; ; attempt++)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        request.Headers.Add("X-Microsoft-OutputFormat", OutputFormat);
        request.Headers.UserAgent.ParseAdd("FeeBilling-InstructionalAudio/1.0");

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var audio = await response.Content.ReadAsByteArrayAsync(ct);
                if (audio.Length > 0)
                {
                    return audio;
                }
            }
            else if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Text to speech failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
            }

            if (attempt == maxAttempts)
            {
                throw new InvalidOperationException($"Text to speech failed after {maxAttempts} attempts: {(int)response.StatusCode}.");
            }

            await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested && attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }
    }
}

static string FindRepoRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "FeeBilling.Modern.slnx"))
            && Directory.Exists(Path.Combine(directory.FullName, "docs", "instructional")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Run from inside the fee-billing-platform-modernization repository.");
}

sealed record Chunk(string Label, string? Heading, List<Segment> Segments, string Ssml, string Hash, int Words, int Characters, int VoiceTags);

sealed record Lesson(string OutputPath, string TagTitle, string TrackNumber, List<Chunk> Chunks);

abstract record Segment;

/// <param name="Text">Speakable, XML-escaped text sent to the service.</param>
/// <param name="Source">The script's own wording (markdown), for captions and slide cues.</param>
sealed record Speech(string Voice, string Text, string Source) : Segment
{
    public int Words => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

sealed record Pause(int Milliseconds) : Segment;

static class Timings
{
    // Must match the breaks ScriptBuilder.ToChunk writes after each speech segment and at the end of a chunk.
    private const double BreakAfterSpeech = 0.45;
    private const double TrailingBreak = 1.2;

    public static void Write(string path, double totalSeconds, List<(Chunk Chunk, double Start, double Duration)> timeline)
    {
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("duration", Math.Round(totalSeconds, 3));
        json.WriteStartArray("chunks");
        foreach (var (chunk, start, duration) in timeline)
        {
            json.WriteStartObject();
            json.WriteString("heading", chunk.Heading);
            json.WriteNumber("start", Math.Round(start, 3));
            json.WriteNumber("duration", Math.Round(duration, 3));
            json.WriteStartArray("segments");

            // Silence is known exactly; spread the rest of the chunk's measured length across its words.
            var silence = chunk.Segments.Sum(s => s is Pause p ? p.Milliseconds / 1000.0 : BreakAfterSpeech) + TrailingBreak;
            var secondsPerWord = Math.Max(0, duration - silence) / Math.Max(1, chunk.Words);
            var time = start;
            foreach (var segment in chunk.Segments)
            {
                if (segment is Speech speech)
                {
                    var length = speech.Words * secondsPerWord;
                    json.WriteStartObject();
                    json.WriteString("voice", speech.Voice);
                    json.WriteString("source", speech.Source);
                    json.WriteNumber("start", Math.Round(time, 3));
                    json.WriteNumber("duration", Math.Round(length, 3));
                    json.WriteEndObject();
                    time += length + BreakAfterSpeech;
                }
                else if (segment is Pause pause)
                {
                    time += pause.Milliseconds / 1000.0;
                }
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}

/// <summary>
/// Turns a lesson's script.md into SSML chunks. The script format is deliberately small:
/// "# NN · Title", then "## Section" headings (one synthesis request each), plain paragraphs (narrator),
/// "**Interviewer:** ..." paragraphs (second voice), "[pause 5s]" breaks, list items, and inline `code`.
/// Tables, fenced code blocks, links and HTML are rejected because they can't be read aloud.
/// </summary>
sealed partial class ScriptBuilder(Lexicon lexicon, string narrator, string interviewer)
{
    public const double WordsPerMinute = 150;
    private const double MaxChunkMinutes = 9;
    private const int MaxVoiceTags = 45;
    private const string InterviewerMarker = "**Interviewer:**";

    public Lesson? Parse(string path, int number, int lessonCount, string outputPath, List<string> errors)
    {
        var lines = File.ReadAllLines(path);
        var name = Path.GetFileName(Path.GetDirectoryName(path))!;
        var startingErrors = errors.Count;
        string? title = null;
        var sections = new List<(string? Heading, List<Segment> Segments)> { (null, []) };
        var paragraph = new List<string>();

        void Error(int line, string message) => errors.Add($"{name}/script.md:{line}: {message}");

        void Flush()
        {
            if (paragraph.Count > 0)
            {
                sections[^1].Segments.AddRange(SpeakParagraph(string.Join(" ", paragraph)));
                paragraph.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();
            var prose = CodeSpan().Replace(line, string.Empty);

            if (line.StartsWith("```", StringComparison.Ordinal)) { Error(lineNumber, "fenced code blocks can't be read aloud; describe the code in prose"); continue; }
            if (line.StartsWith('|')) { Error(lineNumber, "tables can't be read aloud; use sentences"); continue; }
            if (MarkdownLink().IsMatch(prose)) { Error(lineNumber, "links can't be read aloud; name the file or page instead"); continue; }
            if (HtmlTag().IsMatch(prose)) { Error(lineNumber, "raw HTML/XML is not allowed outside inline code"); continue; }
            if (line.Count(c => c == '`') % 2 != 0) { Error(lineNumber, "unbalanced backticks"); continue; }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                if (title is not null) { Error(lineNumber, "only one # title is allowed"); }
                title = TitlePrefix().Replace(line[2..].Trim(), string.Empty);
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                var heading = line[3..].Trim();
                sections.Add((heading, [new Speech(narrator, Speak(heading) + ".", heading), new Pause(900)]));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                Flush();
                sections[^1].Segments.Add(new Speech(narrator, Speak(line[4..].Trim()) + ".", line[4..].Trim()));
                sections[^1].Segments.Add(new Pause(600));
                continue;
            }

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var pause = PauseLine().Match(line);
            if (pause.Success)
            {
                Flush();
                sections[^1].Segments.Add(new Pause(PauseMilliseconds(pause)));
                continue;
            }

            var listItem = ListItem().Match(line);
            if (listItem.Success)
            {
                Flush();
                paragraph.Add(EndSentence(line[listItem.Length..].Trim()));
                Flush();
                continue;
            }

            paragraph.Add(line);
        }

        Flush();

        if (title is null) { Error(1, "missing '# NN · Title' line"); }
        if (sections.Count < 2) { Error(1, "no '## ' sections"); }
        if (errors.Count > startingErrors) { return null; }

        // Spoken intro and outro, so every file announces itself when played out of context.
        sections[0].Segments.Insert(0, new Speech(narrator, Speak($"FeeBilling modernization, lesson {number}. {title}."), $"FeeBilling modernization, lesson {number}. {title}."));
        sections[0].Segments.Insert(1, new Pause(1000));
        sections[^1].Segments.Add(new Pause(800));
        sections[^1].Segments.Add(new Speech(narrator, $"That's the end of lesson {number}.", $"That's the end of lesson {number}."));

        var chunks = new List<Chunk>();
        foreach (var (heading, segments) in sections.Where(s => s.Segments.OfType<Speech>().Any()))
        {
            foreach (var part in SplitForLimits(segments))
            {
                chunks.Add(ToChunk($"{name} #{chunks.Count:00} {heading ?? "intro"}", heading, part));
            }
        }

        return new Lesson(outputPath, $"{number:00} {title}", $"{number}/{lessonCount}", chunks);
    }

    public Lesson BuildPronunciationTest(string outputPath)
    {
        var segments = new List<Segment> { new Speech(narrator, "Pronunciation test. Each term is followed by a short pause.", "Pronunciation test."), new Pause(800) };
        foreach (var term in lexicon.Terms)
        {
            segments.Add(new Speech(narrator, XmlEscape(lexicon.Prose(term)) + ".", term));
            segments.Add(new Pause(400));
        }

        foreach (var code in new[]
        {
            "legacy/FeeBilling.Core/FeeCalculator.cs",
            "Math.Round(span * (double)tier.AnnualRate / 4, 2)",
            "IOptions<BillingOptions>",
            "src/FeeBilling.Gateway/appsettings.json",
            "decimal(18,2)",
            "usp_GetBillableAum",
        })
        {
            segments.Add(new Speech(narrator, XmlEscape(lexicon.Code(code)) + ".", code));
            segments.Add(new Pause(400));
        }

        segments.Add(new Speech(interviewer, "And this is the interviewer voice. How would you approach modernizing a large dot net Framework application?", "Interviewer voice sample."));
        segments.Add(new Pause(3000));
        segments.Add(new Speech(narrator, "That pause was three seconds.", "That pause was three seconds."));

        return new Lesson(outputPath, "Pronunciation test", "0/0", SplitForLimits(segments).Select((p, i) => ToChunk($"pronunciation #{i:00}", null, p)).ToList());
    }

    private IEnumerable<Segment> SpeakParagraph(string markdown)
    {
        var voice = narrator;
        if (markdown.StartsWith(InterviewerMarker, StringComparison.Ordinal))
        {
            voice = interviewer;
            markdown = markdown[InterviewerMarker.Length..].Trim();
        }

        // Inline [pause Ns] markers split a paragraph into speech and silence.
        var position = 0;
        foreach (Match match in InlinePause().Matches(markdown))
        {
            var before = markdown[position..match.Index].Trim();
            if (before.Length > 0) { yield return new Speech(voice, Speak(before), before); }
            yield return new Pause(PauseMilliseconds(match));
            position = match.Index + match.Length;
        }

        var rest = markdown[position..].Trim();
        if (rest.Length > 0) { yield return new Speech(voice, Speak(rest), rest); }
    }

    /// <summary>Markdown to speakable, XML-escaped text: code spans through the code rules, prose through the lexicon.</summary>
    private string Speak(string markdown)
    {
        var spoken = new StringBuilder();
        foreach (var part in CodeSplit().Split(markdown))
        {
            if (part.Length >= 2 && part[0] == '`' && part[^1] == '`')
            {
                spoken.Append(lexicon.Code(part[1..^1]));
            }
            else
            {
                var prose = Emphasis().Replace(part, "$2");
                prose = prose.Replace("·", ",", StringComparison.Ordinal);
                spoken.Append(lexicon.Prose(prose));
            }
        }

        return XmlEscape(Whitespace().Replace(spoken.ToString(), " ").Trim());
    }

    private IEnumerable<List<Segment>> SplitForLimits(List<Segment> segments)
    {
        var current = new List<Segment>();
        var words = 0;
        foreach (var segment in segments)
        {
            var segmentWords = segment is Speech s ? s.Words : 0;
            var tooLong = (words + segmentWords) / WordsPerMinute > MaxChunkMinutes;
            var tooManyVoices = VoiceTags(current.Append(segment)) > MaxVoiceTags;
            if ((tooLong || tooManyVoices) && current.OfType<Speech>().Any())
            {
                yield return current;
                current = [];
                words = 0;
            }

            current.Add(segment);
            words += segmentWords;
        }

        if (current.OfType<Speech>().Any()) { yield return current; }
    }

    private Chunk ToChunk(string label, string? heading, List<Segment> segments)
    {
        var ssml = new StringBuilder();
        ssml.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"https://www.w3.org/2001/mstts\" xml:lang=\"en-US\">");
        string? open = null;
        foreach (var segment in segments)
        {
            var voice = segment is Speech speech ? speech.Voice : open ?? narrator;
            if (voice != open)
            {
                if (open is not null) { ssml.Append("</lang></voice>"); }
                ssml.Append("<voice name=\"").Append(voice).Append("\"><lang xml:lang=\"en-US\">");
                open = voice;
            }

            switch (segment)
            {
                case Speech s:
                    ssml.Append(s.Text).Append(" <break time=\"450ms\"/> ");
                    break;
                case Pause p:
                    // One break element is capped by the service, so long pauses are several in a row.
                    for (var remaining = p.Milliseconds; remaining > 0; remaining -= 5000)
                    {
                        ssml.Append("<break time=\"").Append(Math.Min(remaining, 5000)).Append("ms\"/>");
                    }

                    break;
            }
        }

        // Trailing silence so consecutive sections don't run into each other once the chunks are joined.
        ssml.Append("<break time=\"1200ms\"/></lang></voice></speak>");

        var text = ssml.ToString();
        var speeches = segments.OfType<Speech>().ToList();
        return new Chunk(
            label,
            heading,
            segments,
            text,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            speeches.Sum(s => s.Words),
            speeches.Sum(s => s.Text.Length),
            VoiceTags(segments));
    }

    private int VoiceTags(IEnumerable<Segment> segments)
    {
        var count = 0;
        string? open = null;
        foreach (var segment in segments)
        {
            var voice = segment is Speech s ? s.Voice : open ?? narrator;
            if (voice != open) { count++; open = voice; }
        }

        return count;
    }

    private static int PauseMilliseconds(Match match) =>
        (int)(double.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) * 1000);

    private static string EndSentence(string text) =>
        text.Length > 0 && ".?!:;".Contains(text[^1], StringComparison.Ordinal) ? text : text + ".";

    private static string XmlEscape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex("(`[^`]*`)")]
    private static partial Regex CodeSplit();

    [GeneratedRegex(@"\[[^\]]+\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"<[A-Za-z/!]")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"^\d+\s*·\s*")]
    private static partial Regex TitlePrefix();

    [GeneratedRegex(@"^\[pause (?<seconds>\d+(\.\d+)?)s\]$")]
    private static partial Regex PauseLine();

    [GeneratedRegex(@"\[pause (?<seconds>\d+(\.\d+)?)s\]")]
    private static partial Regex InlinePause();

    [GeneratedRegex(@"^([-*]|\d+\.)\s+")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"(?<![\w*])(\*\*|__|\*|_)(\S(?:.*?\S)?)\1(?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Spoken forms for acronyms, file names and code, loaded from pronunciations.json.</summary>
sealed partial class Lexicon
{
    private readonly Dictionary<string, string> _terms;
    private readonly Dictionary<string, string> _extensions;
    private readonly Regex _termPattern;
    private readonly Regex _extensionPattern;

    private Lexicon(Dictionary<string, string> terms, Dictionary<string, string> extensions)
    {
        _terms = terms;
        _extensions = extensions;

        // Longest first, so "ASP.NET Core" wins over "ASP.NET" and ".NET". One pass, so replacements aren't re-replaced.
        var alternatives = string.Join("|", terms.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        _termPattern = new Regex($"(?<![A-Za-z0-9_])(?:{alternatives})(?![A-Za-z0-9_+#])", RegexOptions.CultureInvariant);
        _extensionPattern = new Regex($@"\.({string.Join("|", extensions.Keys.OrderByDescending(k => k.Length))})\b", RegexOptions.CultureInvariant);
    }

    public IEnumerable<string> Terms => _terms.Keys;

    public static Lexicon Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        static Dictionary<string, string> Read(JsonElement element) =>
            element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);

        return new Lexicon(Read(document.RootElement.GetProperty("terms")), Read(document.RootElement.GetProperty("extensions")));
    }

    public string Prose(string text) => _termPattern.Replace(text, m => _terms[m.Value]);

    public string Code(string code)
    {
        if (_terms.TryGetValue(code, out var exact))
        {
            return exact;
        }

        var s = code;
        for (var i = 0; i < 3; i++)
        {
            s = GenericArguments().Replace(s, " of $1 ");
        }

        s = s.Replace("=>", " goes to ", StringComparison.Ordinal)
            .Replace("==", " equals ", StringComparison.Ordinal)
            .Replace("!=", " not equal to ", StringComparison.Ordinal)
            .Replace("&&", " and ", StringComparison.Ordinal)
            .Replace("||", " or ", StringComparison.Ordinal)
            .Replace("??", " or else ", StringComparison.Ordinal)
            .Replace(" * ", " times ", StringComparison.Ordinal)
            .Replace(" / ", " divided by ", StringComparison.Ordinal)
            .Replace(" + ", " plus ", StringComparison.Ordinal)
            .Replace(" = ", " equals ", StringComparison.Ordinal)
            .Replace("()", string.Empty, StringComparison.Ordinal);

        s = Prose(s);
        s = _extensionPattern.Replace(s, m => " dot " + _extensions[m.Groups[1].Value]);
        s = s.Replace("/", " slash ", StringComparison.Ordinal);
        s = MemberAccess().Replace(s, " dot ");
        s = Punctuation().Replace(s, " ");
        return s.Replace("_", " ", StringComparison.Ordinal);
    }

    [GeneratedRegex("<([^<>]*)>")]
    private static partial Regex GenericArguments();

    [GeneratedRegex(@"(?<=[A-Za-z_)])\.(?=[A-Za-z_])")]
    private static partial Regex MemberAccess();

    [GeneratedRegex(@"[{}\[\]"":]")]
    private static partial Regex Punctuation();
}

static class Mp3
{
    private static readonly int[] Mpeg1Layer3Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Layer3Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    /// <summary>
    /// Returns only the audio frames: drops any ID3v2 tag and the Xing/Info header frame, which would carry
    /// the wrong length once chunks are joined. Constant-bitrate frames in the same format concatenate cleanly.
    /// </summary>
    public static byte[] ExtractFrames(byte[] data, out double seconds)
    {
        var position = 0;
        if (data.Length > 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3')
        {
            position = 10 + ((data[6] & 0x7F) << 21 | (data[7] & 0x7F) << 14 | (data[8] & 0x7F) << 7 | (data[9] & 0x7F));
        }

        using var output = new MemoryStream();
        long samples = 0;
        var sampleRate = 0;
        var first = true;
        while (position + 4 <= data.Length)
        {
            if (!TryReadHeader(data, position, out var length, out var rate, out var samplesPerFrame) || position + length > data.Length)
            {
                position++;
                continue;
            }

            var frame = data.AsSpan(position, length);
            if (first && (frame.IndexOf("Xing"u8) >= 0 || frame.IndexOf("Info"u8) >= 0))
            {
                first = false;
                position += length;
                continue;
            }

            first = false;
            output.Write(frame);
            samples += samplesPerFrame;
            sampleRate = rate;
            position += length;
        }

        seconds = sampleRate == 0 ? 0 : samples / (double)sampleRate;
        return output.ToArray();
    }

    public static byte[] BuildId3Tag(IEnumerable<(string Id, string Value)> frames)
    {
        using var body = new MemoryStream();
        foreach (var (id, value) in frames)
        {
            // Encoding byte 1 = UTF-16 with BOM.
            var text = new byte[] { 1, 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(value)).ToArray();
            body.Write(Encoding.ASCII.GetBytes(id));
            body.Write([(byte)(text.Length >> 24), (byte)(text.Length >> 16), (byte)(text.Length >> 8), (byte)text.Length, 0, 0]);
            body.Write(text);
        }

        var size = (int)body.Length;
        using var tag = new MemoryStream();
        tag.Write("ID3"u8);
        tag.Write([3, 0, 0, (byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)]);
        body.Position = 0;
        body.CopyTo(tag);
        return tag.ToArray();
    }

    private static bool TryReadHeader(byte[] data, int position, out int length, out int sampleRate, out int samplesPerFrame)
    {
        length = sampleRate = samplesPerFrame = 0;
        if (data[position] != 0xFF || (data[position + 1] & 0xE0) != 0xE0)
        {
            return false;
        }

        var version = (data[position + 1] >> 3) & 0x3;   // 0 = MPEG 2.5, 2 = MPEG 2, 3 = MPEG 1
        var layer = (data[position + 1] >> 1) & 0x3;     // 1 = Layer III
        var bitrateIndex = (data[position + 2] >> 4) & 0xF;
        var sampleRateIndex = (data[position + 2] >> 2) & 0x3;
        var padding = (data[position + 2] >> 1) & 0x1;
        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || sampleRateIndex == 3)
        {
            return false;
        }

        var mpeg1 = version == 3;
        var bitrate = (mpeg1 ? Mpeg1Layer3Bitrates : Mpeg2Layer3Bitrates)[bitrateIndex] * 1000;
        sampleRate = version switch
        {
            3 => new[] { 44100, 48000, 32000 }[sampleRateIndex],
            2 => new[] { 22050, 24000, 16000 }[sampleRateIndex],
            _ => new[] { 11025, 12000, 8000 }[sampleRateIndex],
        };
        samplesPerFrame = mpeg1 ? 1152 : 576;
        length = (mpeg1 ? 144 : 72) * bitrate / sampleRate + padding;
        return length > 4;
    }
}
