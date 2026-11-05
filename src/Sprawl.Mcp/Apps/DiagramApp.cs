using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Sprawl.Mcp.Apps;

/// <summary>
/// The MCP App (ui:// resource, MCP Apps extension 2026-01-26) that shows a Sprawl diagram inside the conversation:
/// the server-drawn SVG with pan and zoom, and its walkthroughs stepped through with highlighting. It talks to the host
/// only through the documented postMessage JSON-RPC (ui/initialize, ui/notifications/*, tools/call); it loads nothing
/// from the network, so the host's default restrictive CSP is enough.
/// </summary>
[McpServerResourceType]
[Authorize(Policy = "scope:canvas.read")]
public sealed class DiagramApp
{
    public const string Uri = "ui://sprawl/diagram";
    public const string ToolMeta = """{"resourceUri":"ui://sprawl/diagram"}""";

    [McpServerResource(UriTemplate = Uri, Name = "sprawl-diagram", Title = "Sprawl diagram", MimeType = "text/html;profile=mcp-app")]
    [Description("Shows a Sprawl diagram with pan, zoom and its step-by-step walkthroughs.")]
    public static string Html() => Page;

    private const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Sprawl diagram</title>
<style>
  :root { color-scheme: light dark; --bg: #f7f7f8; --surface: #fff; --text: #1f2328; --muted: #5d636b; --border: #dcdfe3; --accent: #2f6fde; }
  :root.dark { --bg: #1b1c1e; --surface: #2a2c30; --text: #ececee; --muted: #a9adb4; --border: #3a3d42; --accent: #6b9cf0; }
  * { box-sizing: border-box; }
  html, body { margin: 0; background: var(--bg); color: var(--text); font: 14px/1.5 Inter, system-ui, -apple-system, "Segoe UI", sans-serif; }
  header { display: flex; align-items: baseline; gap: 8px; padding: 10px 14px 6px; }
  header h1 { margin: 0; font-size: 15px; font-weight: 600; }
  header span { color: var(--muted); font-size: 12px; }
  #stage { position: relative; height: 420px; overflow: hidden; cursor: grab; touch-action: none; border-top: 1px solid var(--border); border-bottom: 1px solid var(--border); }
  #stage.dragging { cursor: grabbing; }
  #canvas { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
  #canvas svg { display: block; }
  .tools { position: absolute; right: 10px; top: 10px; display: flex; gap: 4px; }
  button { font: inherit; font-size: 13px; color: var(--text); background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 4px 10px; cursor: pointer; }
  button:disabled { opacity: .45; cursor: default; }
  button.primary { background: var(--text); color: var(--bg); border-color: var(--text); }
  #walk { padding: 10px 14px 12px; display: none; }
  #walk.on { display: grid; gap: 8px; }
  #walk .row { display: flex; align-items: center; gap: 8px; }
  #walk select { font: inherit; font-size: 13px; color: var(--text); background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 3px 6px; }
  #step-text { font-size: 15px; line-height: 1.55; min-height: 2.6em; }
  #count { color: var(--muted); font-size: 12px; margin-left: auto; font-variant-numeric: tabular-nums; }
  #empty { padding: 24px 14px; color: var(--muted); }
</style>
</head>
<body>
<header><h1 id="title">Sprawl</h1><span id="sub"></span></header>
<div id="stage" hidden><div id="canvas"></div><div class="tools"><button id="fit" type="button" title="Fit">Fit</button><button id="zin" type="button" aria-label="Zoom in">+</button><button id="zout" type="button" aria-label="Zoom out">−</button></div></div>
<section id="walk" aria-label="Walkthrough">
  <div class="row"><select id="which" aria-label="Walkthrough"></select><span id="count"></span></div>
  <div id="step-text" aria-live="polite"></div>
  <div class="row"><button id="prev" type="button">‹ Back</button><button id="next" type="button" class="primary">Next ›</button><button id="all" type="button">Show all</button></div>
</section>
<p id="empty">Loading the diagram…</p>
<script>
(() => {
  const parent = window.parent;
  let nextId = 1;
  const pending = new Map();
  const send = (msg) => parent.postMessage(msg, "*");
  const request = (method, params) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    send({ jsonrpc: "2.0", id, method, params });
  });
  const notify = (method, params) => send({ jsonrpc: "2.0", method, params });

  const $ = (id) => document.getElementById(id);
  let view = null, walk = -1, step = 0, scale = 1, tx = 0, ty = 0;

  function applyTheme(theme) {
    const dark = theme ? theme === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
    document.documentElement.classList.toggle("dark", dark);
    const svg = $("canvas").querySelector("svg");
    if (svg) { svg.classList.toggle("theme-dark", dark); svg.classList.toggle("theme-light", !dark); }
  }

  function setTransform() { $("canvas").style.transform = `translate(${tx}px, ${ty}px) scale(${scale})`; }
  function fit() {
    const svg = $("canvas").querySelector("svg");
    if (!svg) return;
    const stage = $("stage"), w = svg.viewBox.baseVal.width, h = svg.viewBox.baseVal.height;
    scale = Math.min(stage.clientWidth / w, stage.clientHeight / h, 1.25);
    tx = (stage.clientWidth - w * scale) / 2; ty = (stage.clientHeight - h * scale) / 2;
    setTransform();
  }
  function zoom(factor, cx, cy) {
    const stage = $("stage");
    cx ??= stage.clientWidth / 2; cy ??= stage.clientHeight / 2;
    const next = Math.min(4, Math.max(0.1, scale * factor));
    tx = cx - (cx - tx) * next / scale; ty = cy - (cy - ty) * next / scale; scale = next; setTransform();
  }

  function lit(ids, marks) {
    const svg = $("canvas").querySelector("svg");
    const on = new Set([...ids, ...marks.map((m) => m.target)]);
    const states = new Map(marks.map((m) => [m.target, m.state]));
    for (const id of ids) for (const el of svg.querySelectorAll(`[data-group="${CSS.escape(id)}"]`)) on.add(el.dataset.id);
    const edges = new Set();
    for (const el of svg.querySelectorAll(".edge")) {
      if (on.has(el.dataset.id)) { on.add(el.dataset.from); on.add(el.dataset.to); edges.add(el.dataset.id); }
      else if (on.has(el.dataset.from) && on.has(el.dataset.to)) edges.add(el.dataset.id);
    }
    for (const el of svg.querySelectorAll(".item")) {
      const isLit = el.classList.contains("edge") ? edges.has(el.dataset.id) : on.has(el.dataset.id);
      el.classList.toggle("lit", isLit); el.classList.toggle("dim", !isLit && !el.classList.contains("group") && !el.classList.contains("loop"));
      for (const c of [...el.classList]) if (c.startsWith("mark-")) el.classList.remove(c);
      if (states.has(el.dataset.id)) el.classList.add("mark-" + states.get(el.dataset.id));
    }
  }

  function clearMarks() {
    for (const el of $("canvas").querySelectorAll("[class*='mark-']")) for (const c of [...el.classList]) if (c.startsWith("mark-")) el.classList.remove(c);
  }

  function showStep() {
    const svg = $("canvas").querySelector("svg");
    const w = view.walkthroughs[walk];
    if (!w) { svg.classList.remove("walking"); clearMarks(); return; }
    const s = w.steps[step];
    svg.classList.add("walking");
    lit(s.highlight || [], s.marks || []);
    $("step-text").textContent = s.text.replace(/\*\*|__|`/g, "");
    $("count").textContent = `${step + 1} / ${w.steps.length}`;
    $("prev").disabled = step === 0;
    $("next").textContent = step === w.steps.length - 1 ? "Finish" : "Next ›";
  }

  function show(data) {
    view = data;
    $("title").textContent = data.title;
    $("sub").textContent = data.canvasTitle + " · v" + data.version;
    $("canvas").innerHTML = data.svg;
    $("stage").hidden = false; $("empty").hidden = true;
    applyTheme(hostTheme);
    const walks = data.walkthroughs || [];
    $("walk").classList.toggle("on", walks.length > 0);
    $("which").innerHTML = "";
    walks.forEach((w, i) => { const o = document.createElement("option"); o.value = i; o.textContent = `${w.title} (${w.steps.length} steps)`; $("which").append(o); });
    walk = walks.length ? 0 : -1; step = 0;
    requestAnimationFrame(() => { fit(); if (walk >= 0) showStep(); reportSize(); });
  }

  function reportSize() { notify("ui/notifications/size-changed", { width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }); }

  function fromResult(result) {
    const sc = result && result.structuredContent;
    const data = sc && (sc.data || sc);
    if (!data) return null;
    if (data.svg) return { view: data };
    // add_diagram returns refs; ask the server for the drawing of the new diagram region.
    if (data.canvasId && data.refs && data.refs.d) return { canvasId: data.canvasId, region: data.refs.d };
    return null;
  }

  async function handleResult(result) {
    const found = fromResult(result);
    if (!found) { $("empty").textContent = "Nothing to show."; return; }
    if (found.view) { show(found.view); return; }
    try {
      const shown = await request("tools/call", { name: "show_diagram", arguments: { canvasId: found.canvasId, region: found.region } });
      const again = fromResult(shown);
      if (again && again.view) show(again.view); else $("empty").textContent = "The diagram is on the canvas.";
    } catch { $("empty").textContent = "The diagram is on the canvas; open Sprawl to see it."; }
  }

  let hostTheme = null;
  window.addEventListener("message", (event) => {
    if (event.source !== parent) return;
    const msg = event.data;
    if (!msg || msg.jsonrpc !== "2.0") return;
    if (msg.id !== undefined && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id); pending.delete(msg.id);
      msg.error ? reject(msg.error) : resolve(msg.result);
      return;
    }
    if (msg.method === "ui/notifications/tool-result") handleResult(msg.params);
    else if (msg.method === "ui/notifications/host-context-changed" && msg.params && msg.params.theme) { hostTheme = msg.params.theme; applyTheme(hostTheme); }
  });

  // Pan and zoom.
  const stage = $("stage");
  let drag = null;
  stage.addEventListener("pointerdown", (e) => { if (e.target.closest("button")) return; drag = { x: e.clientX - tx, y: e.clientY - ty }; stage.setPointerCapture(e.pointerId); stage.classList.add("dragging"); });
  stage.addEventListener("pointermove", (e) => { if (!drag) return; tx = e.clientX - drag.x; ty = e.clientY - drag.y; setTransform(); });
  stage.addEventListener("pointerup", () => { drag = null; stage.classList.remove("dragging"); });
  stage.addEventListener("wheel", (e) => { e.preventDefault(); const r = stage.getBoundingClientRect(); zoom(Math.exp(-e.deltaY * 0.0015), e.clientX - r.left, e.clientY - r.top); }, { passive: false });
  $("fit").onclick = fit; $("zin").onclick = () => zoom(1.25); $("zout").onclick = () => zoom(0.8);
  $("prev").onclick = () => { if (step > 0) { step--; showStep(); } };
  $("next").onclick = () => { const w = view.walkthroughs[walk]; if (step < w.steps.length - 1) { step++; showStep(); } else { $("canvas").querySelector("svg").classList.remove("walking"); clearMarks(); } };
  $("all").onclick = () => { $("canvas").querySelector("svg").classList.remove("walking"); clearMarks(); };
  $("which").onchange = (e) => { walk = Number(e.target.value); step = 0; showStep(); };
  document.addEventListener("keydown", (e) => { if (!view || walk < 0) return; if (e.key === "ArrowRight") $("next").click(); if (e.key === "ArrowLeft") $("prev").click(); });
  window.addEventListener("resize", () => fit());

  request("ui/initialize", { protocolVersion: "2026-01-26", appCapabilities: {}, appInfo: { name: "Sprawl", version: "1.0" } })
    .then((init) => { hostTheme = init && init.hostContext && init.hostContext.theme || null; applyTheme(hostTheme); notify("ui/notifications/initialized", {}); })
    .catch(() => notify("ui/notifications/initialized", {}));
})();
</script>
</body>
</html>
""";
}
