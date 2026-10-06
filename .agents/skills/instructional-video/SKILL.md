---
name: instructional-video
description: Create or update an instructional video lesson in docs/instructional/ for the FeeBilling .NET Framework → .NET 10 migration series. Writes the narration script (script.md), the video outline (README.md) and the timed slide deck (slides.html), validates them, then builds the MP3 and 1080p captioned MP4 with tools/instructional-audio/generate.cs and tools/instructional-video/build.cs. Use when asked to make, add, record, script, regenerate or fix a lesson, tutorial, walkthrough, screencast, training video or slide deck for this repository.
---

# Instructional video

A lesson video in this repository is **narration audio + a slide deck timed to it + captions**, all generated from text files checked into the lesson folder. You author three text files; two tools turn them into media.

```
docs/instructional/NN-kebab-topic/
  script.md      transcript the audio is synthesized from  (you write)
  slides.html    one <section> per slide, each cued to a phrase in script.md  (you write)
  README.md      video outline: objectives, questions, files on screen, run sheet  (you write)
  NN-kebab-topic.mp3   tools/instructional-audio/generate.cs  (Git LFS)
  NN-kebab-topic.mp4   tools/instructional-video/build.cs     (Git LFS)
```

Before writing anything, read `docs/instructional/README.md` and one complete existing lesson (lesson `01-migration-strategy-strangler-fig` is the reference) so tone, length and structure match. Read the code the lesson is about: every file path, class name, number and quote in a lesson must be verified against the repository, not recalled.

## Workflow

1. **Scope the lesson.** One migration problem per lesson. Pick the next free `NN`, a kebab-case folder name, the work package(s) (WP-01..WP-10 in `docs/brasswick-modernization-training-plan.md`), and the single "core interview question" it answers. If an existing lesson already covers the topic, update it instead of adding a new one.
2. **Research the code.** Find where the problem lives in `legacy/` (the "before", as found — never edit it for a lesson) and what the target looks like in `src/` or the plan. Note exact paths and short excerpts for slides.
3. **Write `script.md`** (format below). Target ~20 minutes ≈ 3,000 words at 150 wpm.
4. **Write `README.md`** (the video outline, structure below).
5. **Write `slides.html`** (structure below), cueing each slide to a verbatim phrase in the script.
6. **Validate without network or keys:**
   ```bash
   dotnet run tools/instructional-audio/generate.cs -- --dry-run      # script format, length, cost estimate
   ```
   Fix every reported error. Then open `slides.html` in a browser (or render it, step 8) and check the layout.
7. **Synthesize the audio** (needs `AZURE_SPEECH_KEY`, optional `AZURE_SPEECH_REGION`, default `eastus2`). Never write a key into the repo or a command you echo; if no key is available, stop here and tell the user exactly which command to run.
   ```bash
   dotnet run tools/instructional-audio/generate.cs -- NN
   ```
   This also writes the timing manifest `tools/instructional-audio/.cache/timings/<folder>.json` that the video is synced to. Add new acronyms or code identifiers to `tools/instructional-audio/pronunciations.json` and check them with `-- --pronunciation-test`.
8. **Build the video** (needs Chrome/Edge — `EDGE_PATH` overrides; in a Linux cloud container point it at a Chromium binary, e.g. `EDGE_PATH=/opt/pw-browsers/chromium` — and ffmpeg with libx264, `FFMPEG_PATH` overrides):
   ```bash
   dotnet run tools/instructional-video/build.cs -- NN --check        # every data-cue found, prints the schedule
   dotnet run tools/instructional-video/build.cs -- NN --slides-only  # also renders PNGs to tools/instructional-video/.cache/<folder>/
   dotnet run tools/instructional-video/build.cs -- NN                # encodes <folder>.mp4 with captions
   ```
   Look at the rendered PNGs (Read tool) before encoding: overflowing code, clipped tables and unreadable text are the common failures.
9. **Verify the output:** `ffprobe -v error -show_entries format=duration:stream=codec_name,width,height <folder>.mp4` → 1920×1080, h264 + audio, duration ≈ the MP3. Spot-check a few frames at cue times with `ffmpeg -ss <t> -i <mp4> -frames:v 1 <scratch>.png`.
10. **Register the lesson** in the series table in `docs/instructional/README.md` (Lesson, MP3 · Video · Transcript links, work package, core question) and in "Suggested orders" if relevant.
11. **Commit.** MP3/MP4 are Git LFS (`.gitattributes`); make sure `git lfs` is installed so they aren't committed as blobs. `.cache/` folders are gitignored — never commit them.

## `script.md` format (parsed strictly by generate.cs)

- First line `# NN · Title` (middle dot `·`).
- `## Section` headings; each section is one synthesis request and must stay under ~9 minutes of speech.
- Plain paragraphs and `-` list items are spoken by the narrator. `**Interviewer:** ...` paragraphs use the second voice.
- `[pause 5s]` on its own line inserts silence.
- Inline `` `code` `` is allowed and spoken through the pronunciation lexicon.
- **Rejected:** tables, fenced code blocks, links, HTML. It must make sense with eyes closed: describe code, don't read it. Spell numbers the way they should be said when it matters ("thirty percent", "an Order of one thousand").

Standard section order (keep it — the series promises it):

1. Untitled intro paragraph(s) after the `#` title: what the lesson covers and why it matters.
2. `## The questions this lesson answers` — the interview questions, then the one sentence to hold onto.
3. Concept sections — the problem in general terms.
4. A FeeBilling section — where it lives in this repo, with exact paths.
5. The modern approach — the target design and the steps.
6. `## Traps` — what goes wrong silently.
7. `## Interview drill` — one intro paragraph, then per question: `**Interviewer:** question`, `[pause 5s]`, model answer paragraph.
8. `## Recap` — "Five things to remember", numbered One..Five, then a one-sentence preview of the next lesson.

Voice: second person, conversational, concrete, opinionated with trade-offs stated out loud. No filler, no marketing tone. Time-sensitive facts (support dates, licences, tool status) carry "as of <month year>".

## `README.md` (video outline) structure

```markdown
# NN · Title

> **Runtime:** ~20 min · **Level:** Senior / SME · **Work packages:** WP-xx · **Prerequisites:** lesson NN

**Video:** [NN-topic.mp4](NN-topic.mp4) · [Slides](slides.html) · **Audio lesson:** [NN-topic.mp3](NN-topic.mp3) · [Transcript](script.md)

## Why this video exists
## Learning objectives            (bulleted "By the end, the viewer can:")
## Interview questions this prepares you for   (| Question | What a strong answer includes |)
## FeeBilling code on screen      (| File | What to show |)
## Run sheet                      (| Time | Segment | Content |, mm:ss–mm:ss summing to the runtime)
## Demo commands                  (exact, copy-pasteable)
## Traps
## References                     (official docs; no invented URLs)
```

## `slides.html` structure

Copy the head and footer of an existing deck and keep the shared runtime — never inline new global styles:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Lesson NN Slides</title>
<link rel="stylesheet" href="../assets/slides.css">
</head>
<body data-lesson-number="NN" data-lesson-title="Title" data-parts="Introduction|The questions|...|Interview drill|Recap">

<section id="title" data-part="0">
  <div class="kicker">FeeBilling modernization · Lesson NN</div>
  <h1>Title</h1>
  <p class="lead">One-line subtitle</p>
</section>

<section id="questions" data-part="1" data-cue="The questions this lesson answers">
  <h2>The questions this lesson answers</h2>
  <ul><li>...</li></ul>
</section>

<!-- ... -->
<script src="../assets/slides.js"></script>
</body>
</html>
```

Rules:

- **Every slide except the first has a `data-cue`**: a phrase copied *verbatim* from `script.md` (a heading or the start of a sentence), unique in the script, and the cues must appear **in narration order**. A slide appears when the narration reaches its cue. `build.cs --check` reports any cue it can't find.
- `data-part` is the index into `data-parts` (drives the header label and progress bar). Parts should line up with `##` sections.
- Aim for one slide every 20–40 seconds (~35–60 slides for 20 minutes). Cue changes inside a section are estimated from word counts and land within a second or two; section boundaries are exact.
- Slides support the narration, they don't transcribe it: a heading, 3–5 short bullets, a diagram, or a short code excerpt.
- Code: `<pre class="code" data-lang="cs|json|yaml|sql|sh|md|xml" data-mark="2,5">` (HTML-escape `<`, `>`, `&`); add `tight` for longer excerpts. Keep excerpts ≤ ~16 lines and copy them from the real file.
- Progressive builds: `<template id="x">` with `class="item"` children, then `<section data-template="x" data-show="3">` (reveal) or `data-highlight="2-4">` (focus).
- Reuse existing classes from `docs/instructional/assets/slides.css`: `kicker`, `lead`, `quote`, `accent`, `muted`, `small`, `cols`/`cols3`, `card legacy` / `card modern`, `amber`/`green`/`red`, `file`, `tag`, `risk high|med|low`, `checks`, `steps`/`step item done|next`, `timeline`, `question`, `hint`, `recap`, `num`. Add a class to `slides.css` only if no existing one fits, and check it doesn't change other decks.
- The drill: one slide per question (`class="question"`, cued to the interviewer's line) and optionally a model-answer slide cued to the answer's first words.

## Quality checklist

- [ ] Every path, identifier, number and quote checked against the repo.
- [ ] `generate.cs --dry-run` clean; estimated length 17–22 minutes.
- [ ] `build.cs NN --check` clean; schedule has no slide shorter than ~4 s or longer than ~90 s.
- [ ] Rendered PNGs reviewed: nothing clipped or overflowing at 1920×1080.
- [ ] MP4 is 1080p with audio and captions; duration matches the MP3.
- [ ] Series table in `docs/instructional/README.md` updated.
- [ ] No secrets, no `.cache/` files, media committed through LFS.

## When the tooling can't run

If `dotnet`, a speech key, a browser or ffmpeg isn't available, still deliver the three text files, run whatever validation is possible, and tell the user precisely which commands remain and what they need. Never fabricate an MP3/MP4, and never claim media was built or checked when it wasn't.
