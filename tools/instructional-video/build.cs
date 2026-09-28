#:property PublishAot=false

// Builds lesson videos: the lesson's slides.html, one screenshot per slide, timed to the narration MP3,
// plus captions from the script. Output: docs/instructional/<lesson>/<lesson>.mp4
//
//   dotnet run tools/instructional-video/build.cs -- 01 02 03           build these lessons
//   dotnet run tools/instructional-video/build.cs -- 02 --check         validate cues and print the schedule only
//   dotnet run tools/instructional-video/build.cs -- 02 --slides-only   also render the PNGs (for review), no encode
//
// Needs, per lesson: slides.html (each <section> has a data-cue phrase from script.md), the lesson MP3, and
// the timing manifest tools/instructional-audio/generate.cs writes to tools/instructional-audio/.cache/timings/.
// Tools: Microsoft Edge or Chrome (EDGE_PATH to override) for screenshots, ffmpeg with libx264 (FFMPEG_PATH to override).

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var checkOnly = args.Contains("--check");
var slidesOnly = args.Contains("--slides-only");
var lessons = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
if (lessons.Count == 0)
{
    Console.Error.WriteLine("Usage: dotnet run tools/instructional-video/build.cs -- <lesson-number>... [--check | --slides-only]");
    return 1;
}

var repoRoot = FindRepoRoot();
var lessonsRoot = Path.Combine(repoRoot, "docs", "instructional");
var failed = 0;
foreach (var lesson in lessons)
{
    try
    {
        failed += BuildLesson(lesson) == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException)
    {
        Console.Error.WriteLine($"{lesson}: {ex.Message}");
        failed++;
    }
}

return failed == 0 ? 0 : 1;

int BuildLesson(string lesson)
{
    var matches = Directory.GetDirectories(lessonsRoot)
        .Where(d => Path.GetFileName(d).StartsWith(lesson, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (matches.Count != 1)
    {
        Console.Error.WriteLine($"'{lesson}' matches {matches.Count} lesson folders; expected exactly one.");
        return 1;
    }

    var lessonDirectory = matches[0];
    var name = Path.GetFileName(lessonDirectory);
    var slidesPath = Path.Combine(lessonDirectory, "slides.html");
    var audioPath = Path.Combine(lessonDirectory, name + ".mp3");
    var timingsPath = Path.Combine(repoRoot, "tools", "instructional-audio", ".cache", "timings", name + ".json");
    var workDirectory = Path.Combine(repoRoot, "tools", "instructional-video", ".cache", name);
    var outputPath = Path.Combine(lessonDirectory, name + ".mp4");

    foreach (var required in new[] { slidesPath, audioPath, timingsPath })
    {
        if (!File.Exists(required))
        {
            Console.Error.WriteLine($"{name}: missing {Path.GetRelativePath(repoRoot, required)}. " +
                (required == timingsPath ? "Run tools/instructional-audio/generate.cs for this lesson first." : string.Empty));
            return 1;
        }
    }

    var (totalSeconds, segments) = Narration.Load(timingsPath);
    var slides = Slides.Parse(File.ReadAllText(slidesPath));
    var schedule = Slides.Schedule(slides, segments, totalSeconds, out var scheduleErrors);
    if (scheduleErrors.Count > 0)
    {
        scheduleErrors.ForEach(e => Console.Error.WriteLine($"{name}: {e}"));
        return 1;
    }

    Console.WriteLine($"{name}: {slides.Count} slides over {TimeSpan.FromSeconds(totalSeconds):mm\\:ss} of narration.");
    for (var i = 0; i < slides.Count; i++)
    {
        Console.WriteLine($"  {TimeSpan.FromSeconds(schedule[i].Start):mm\\:ss}  {schedule[i].Duration,6:0.0}s  {slides[i].Id}");
    }

    if (checkOnly)
    {
        return 0;
    }

    // 1. Screenshots, four browsers at a time, each with its own profile. Only one build renders at a time on the
    //    machine: several builds each running four browsers exhaust memory and slow every screenshot to a crawl.
    Directory.CreateDirectory(workDirectory);
    var browser = Tools.FindBrowser();
    var slidesUri = new Uri(slidesPath).AbsoluteUri;
    const int workers = 4;
    using (var renderLock = new Mutex(false, "FeeBilling.InstructionalVideo.Render"))
    {
        try
        {
            if (!renderLock.WaitOne(0))
            {
                Console.WriteLine($"{name}: waiting for another build to finish rendering…");
                renderLock.WaitOne();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous holder was killed mid-render; the lock is now ours.
        }

        try
        {
            Parallel.For(0, workers, worker =>
            {
                var profile = Path.Combine(workDirectory, $"browser-profile-{worker}");
                for (var i = worker; i < slides.Count; i += workers)
                {
                    var png = Path.Combine(workDirectory, $"slide-{i:000}.png");
                    File.Delete(png);
                    Tools.Run(browser,
                    [
                        "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                        "--window-size=1920,1080", $"--user-data-dir={profile}", $"--screenshot={png}", $"{slidesUri}#{slides[i].Id}",
                    ]);
                }
            });
        }
        finally
        {
            renderLock.ReleaseMutex();
        }
    }

    var missing = Enumerable.Range(0, slides.Count).Where(i => !File.Exists(Path.Combine(workDirectory, $"slide-{i:000}.png"))).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"{name}: screenshots failed for {string.Join(", ", missing.Select(i => slides[i].Id))}.");
        return 1;
    }

    Console.WriteLine($"{name}: rendered {slides.Count} slides to {Path.GetRelativePath(repoRoot, workDirectory)}");
    if (slidesOnly)
    {
        return 0;
    }

    // 2. Slide list for ffmpeg's concat demuxer. The last entry is repeated because the demuxer ignores its duration.
    var concat = new StringBuilder("ffconcat version 1.0\n");
    for (var i = 0; i < slides.Count; i++)
    {
        concat.Append(CultureInfo.InvariantCulture, $"file 'slide-{i:000}.png'\nduration {schedule[i].Duration:0.000}\n");
    }

    concat.Append(CultureInfo.InvariantCulture, $"file 'slide-{slides.Count - 1:000}.png'\n");
    var concatPath = Path.Combine(workDirectory, "slides.ffconcat");
    File.WriteAllText(concatPath, concat.ToString());

    // 3. Captions, from the script's own wording rather than the spoken forms.
    var captionsPath = Path.Combine(workDirectory, name + ".srt");
    File.WriteAllText(captionsPath, Captions.ToSrt(segments), new UTF8Encoding(false));

    // 4. Encode. Slides are still images, so 12 fps with a keyframe every 10 s is plenty and encodes quickly.
    var title = Regex.Replace(File.ReadLines(Path.Combine(lessonDirectory, "script.md")).First(), @"^#\s*", string.Empty);
    Tools.Run(Tools.FindFfmpeg(),
    [
        "-y", "-hide_banner", "-loglevel", "warning", "-stats",
        "-f", "concat", "-safe", "0", "-i", concatPath,
        "-i", audioPath,
        "-i", captionsPath,
        "-map", "0:v", "-map", "1:a", "-map", "2:s",
        "-vf", "fps=12,format=yuv420p",
        "-c:v", "libx264", "-preset", "veryfast", "-tune", "stillimage", "-crf", "20", "-g", "120",
        "-c:a", "aac", "-b:a", "128k",
        "-c:s", "mov_text", "-metadata:s:s:0", "language=eng",
        "-metadata", $"title=FeeBilling Modernization {title}",
        "-t", totalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
        "-movflags", "+faststart",
        outputPath,
    ]);

    Console.WriteLine($"{name}: wrote {Path.GetRelativePath(repoRoot, outputPath)}  {new FileInfo(outputPath).Length / 1_048_576.0:0.0} MB");
    return 0;
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

sealed record Segment(string Voice, string Source, double Start, double Duration);

sealed record Slide(string Id, string? Cue);

sealed record Scheduled(double Start, double Duration);

static class Narration
{
    public static (double Total, List<Segment> Segments) Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var segments = root.GetProperty("chunks").EnumerateArray()
            .SelectMany(chunk => chunk.GetProperty("segments").EnumerateArray())
            .Select(s => new Segment(
                s.GetProperty("voice").GetString() ?? string.Empty,
                s.GetProperty("source").GetString() ?? string.Empty,
                s.GetProperty("start").GetDouble(),
                s.GetProperty("duration").GetDouble()))
            .ToList();
        return (root.GetProperty("duration").GetDouble(), segments);
    }
}

static partial class Slides
{
    public static List<Slide> Parse(string html) =>
        SectionTag().Matches(html)
            .Select(m => Attribute().Matches(m.Groups["attributes"].Value)
                .ToDictionary(a => a.Groups["name"].Value, a => WebUtility.HtmlDecode(a.Groups["value"].Value)))
            .Where(a => a.ContainsKey("id"))
            .Select(a => new Slide(a["id"], a.GetValueOrDefault("data-cue")))
            .ToList();

    /// <summary>
    /// Each slide starts when the narration reaches its cue: the paragraph containing the phrase, plus the
    /// fraction of that paragraph's words spoken before it. Cues must appear in narration order.
    /// </summary>
    public static List<Scheduled> Schedule(List<Slide> slides, List<Segment> segments, double total, out List<string> errors)
    {
        errors = [];
        var starts = new List<double>();
        var searchFrom = 0;
        foreach (var slide in slides)
        {
            if (slide.Cue is null)
            {
                if (starts.Count > 0) { errors.Add($"Slide '{slide.Id}' has no data-cue; only the first slide may omit it."); }
                starts.Add(0);
                continue;
            }

            var cue = Normalize(slide.Cue);
            var found = false;
            for (var i = searchFrom; i < segments.Count && !found; i++)
            {
                var source = Normalize(segments[i].Source);
                var at = source.IndexOf(cue, StringComparison.Ordinal);
                if (at < 0) { continue; }

                var wordsBefore = WordCount(source[..at]);
                var start = segments[i].Start + segments[i].Duration * wordsBefore / Math.Max(1, WordCount(source));
                if (starts.Count > 0 && start < starts[^1])
                {
                    errors.Add($"Slide '{slide.Id}' cue is earlier in the narration than the slide before it.");
                }

                starts.Add(start);
                searchFrom = i;
                found = true;
            }

            if (!found) { errors.Add($"Slide '{slide.Id}': cue \"{slide.Cue}\" not found in the narration after the previous slide."); }
        }

        return starts.Select((start, i) => new Scheduled(start, (i + 1 < starts.Count ? starts[i + 1] : total) - start)).ToList();
    }

    public static string Normalize(string text) =>
        Whitespace().Replace(text.Replace("`", string.Empty, StringComparison.Ordinal).Replace("*", string.Empty, StringComparison.Ordinal), " ").Trim();

    private static int WordCount(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"<section\b(?<attributes>[^>]*)>")]
    private static partial Regex SectionTag();

    [GeneratedRegex(@"(?<name>[\w-]+)=""(?<value>[^""]*)""")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

static partial class Captions
{
    private const int MaxWords = 14;

    /// <summary>Sentences (split further to at most ~14 words), each timed by its share of its paragraph's words.</summary>
    public static string ToSrt(List<Segment> segments)
    {
        var srt = new StringBuilder();
        var index = 1;
        foreach (var segment in segments)
        {
            var text = Slides.Normalize(segment.Source);
            var pieces = Sentence().Split(text).Where(s => s.Length > 0).SelectMany(Split).ToList();
            var totalWords = Math.Max(1, pieces.Sum(p => p.Split(' ').Length));
            var time = segment.Start;
            foreach (var piece in pieces)
            {
                var length = segment.Duration * piece.Split(' ').Length / totalWords;
                srt.Append(CultureInfo.InvariantCulture, $"{index++}\n{Stamp(time)} --> {Stamp(time + length)}\n{piece}\n\n");
                time += length;
            }
        }

        return srt.ToString();
    }

    private static IEnumerable<string> Split(string sentence)
    {
        var words = sentence.Split(' ');
        if (words.Length <= MaxWords)
        {
            yield return sentence;
            yield break;
        }

        // Break at a comma near the middle if there is one, otherwise every MaxWords words.
        var parts = (int)Math.Ceiling(words.Length / (double)MaxWords);
        var size = (int)Math.Ceiling(words.Length / (double)parts);
        for (var start = 0; start < words.Length;)
        {
            var end = Math.Min(words.Length, start + size);
            for (var i = end - 1; i > start + size / 2 && end < words.Length; i--)
            {
                if (words[i].EndsWith(',')) { end = i + 1; break; }
            }

            yield return string.Join(' ', words[start..end]);
            start = end;
        }
    }

    private static string Stamp(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<=[.?!])\s+(?=[A-Z0-9""'])")]
    private static partial Regex Sentence();
}

static class Tools
{
    public static string FindBrowser()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("EDGE_PATH"),
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            "/usr/bin/microsoft-edge", "/usr/bin/google-chrome", "/usr/bin/chromium",
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            ?? throw new InvalidOperationException("No Edge or Chrome found. Set EDGE_PATH.");
    }

    public static string FindFfmpeg() => Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";

    // Output is not redirected: the browser can leave helper processes holding redirected pipes open,
    // and ffmpeg's progress is worth seeing.
    public static void Run(string fileName, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} exited with {process.ExitCode}.");
        }
    }
}
