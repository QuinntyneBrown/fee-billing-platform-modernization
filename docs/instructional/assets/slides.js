// Shared runtime for the lesson slide decks (docs/instructional/NN-*/slides.html). Load it at the end of <body>.
//
//   <body data-lesson-number="02" data-lesson-title="..." data-parts="Introduction|The questions|...">
//   <section id="..." data-part="1" data-cue="phrase from script.md"> ... </section>
//
// Also supported:
//   <pre class="code" data-lang="cs|json|yaml|sql|sh|md|xml" data-mark="2,5">   syntax colouring, marked lines
//   <template id="x"> ... elements with class="item" ... </template>
//   <section data-template="x" data-show="3">        items 4+ faded (progressive reveal)
//   <section data-template="x" data-highlight="2-4">  items 2 to 4 highlighted, the rest dimmed
(() => {
  const body = document.body;
  const parts = (body.dataset.parts ?? "").split("|");

  const chrome = document.createElement("div");
  chrome.className = "chrome";
  chrome.innerHTML = `<span><b>${body.dataset.lessonNumber}</b> · ${body.dataset.lessonTitle}</span><span id="part-name"></span>`;
  const progress = document.createElement("div");
  progress.className = "progress";
  progress.innerHTML = parts.map(() => "<i></i>").join("");
  body.append(chrome, progress);

  document.querySelectorAll("section[data-template]").forEach(section => {
    section.innerHTML = document.getElementById(section.dataset.template).innerHTML;
    const items = [...section.querySelectorAll(".item")];
    if (section.dataset.show !== undefined) {
      const shown = Number(section.dataset.show);
      items.forEach((item, i) => item.classList.toggle("off", i >= shown));
    }
    if (section.dataset.highlight !== undefined) {
      const [from, to] = section.dataset.highlight.split("-").map(Number);
      items.forEach((item, i) => item.classList.add(i + 1 >= from && i + 1 <= (to ?? from) ? "on" : "dim"));
    }
  });

  function highlight(code, lang) {
    const escape = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const keywords = {
      cs: "var|new|using|public|private|protected|internal|sealed|class|record|interface|static|readonly|return|if|else|foreach|for|in|await|async|double|decimal|string|int|long|bool|void|true|false|null|throw|catch|try",
      json: "true|false|null",
      yaml: "true|false",
      sql: "CREATE|TABLE|PROCEDURE|SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|SET|NOT|NULL|AND|EXISTS|BEGIN|END|decimal|int|varchar|nvarchar|datetime|date|bit",
      ts: "import|export|const|let|class|return|if|new|true|false|null|this|readonly",
      js: "function|var|const|let|return|if|new|true|false|null|this",
    }[lang];
    const comment = { yaml: "#.*$", sh: "#.*$", sql: "--.*$", md: "(?!x)x", xml: "<!--.*?-->", html: "<!--.*?-->" }[lang] ?? "\\/\\/.*$";
    // Single-quoted strings only where apostrophes aren't prose.
    const single = ["md", "xml", "html"].includes(lang) ? "" : "|'(?:[^'\\\\\\n]|\\\\.)*'";
    const pattern = new RegExp(`(${comment})|("(?:[^"\\\\]|\\\\.)*"${single})|\\b(${keywords ?? "(?!x)x"})\\b|(\\b\\d[\\d_,.]*\\b)`, "gm");
    let html = "", last = 0;
    code.replace(pattern, (match, c, s, k, n, index) => {
      html += escape(code.slice(last, index));
      html += `<span class="${c ? "c" : s ? "s" : k ? "k" : "n"}">${escape(match)}</span>`;
      last = index + match.length;
      return match;
    });
    return html + escape(code.slice(last));
  }

  document.querySelectorAll("pre.code").forEach(pre => {
    const marks = (pre.dataset.mark ?? "").split(",").filter(Boolean).map(Number);
    pre.innerHTML = highlight(pre.textContent, pre.dataset.lang).split("\n")
      .map((line, i) => marks.includes(i + 1) ? `<span class="mark">${line || " "}</span>` : line).join("\n");
  });

  const slides = [...document.querySelectorAll("section")];

  function show(id) {
    const slide = slides.find(s => s.id === id) ?? slides[0];
    slides.forEach(s => s.classList.toggle("active", s === slide));
    const part = Number(slide.dataset.part);
    document.getElementById("part-name").textContent = parts[part] ?? "";
    [...progress.children].forEach((bar, i) => bar.className = i < part ? "done" : i === part ? "now" : "");
  }

  function fit() {
    const scale = Math.min(innerWidth / 1920, innerHeight / 1080);
    body.style.transform = scale < 1 ? `scale(${scale})` : "";
  }

  addEventListener("hashchange", () => show(location.hash.slice(1)));
  addEventListener("resize", fit);
  addEventListener("keydown", e => {
    const i = slides.findIndex(s => s.classList.contains("active"));
    const next = e.key === "ArrowRight" || e.key === " " ? i + 1 : e.key === "ArrowLeft" ? i - 1 : i;
    if (next !== i && slides[next]) location.hash = slides[next].id;
  });
  show(location.hash.slice(1));
  fit();
})();
