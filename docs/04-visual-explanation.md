# Making Sprawl explain anything visually: research and plan

Written 5 November 2026. **Status: V1–V3 built; V4 drill-down and scenarios, V5 tables, charts and causal loops, V6 widgets, V7 sketch style and icons, and V8 teach, quiz and map checks built ([§4](#4-what-was-built)); aliases, stock-and-flow simulation and image cards not started.** The question: what would make Sprawl the most capable place for Claude (or any MCP client) to explain a topic visually? This surveys the tools and research that do the most with visual explanation, and ranks what to add. The fixed decisions in [01-design.md](01-design.md) and the diagram model in [03-features.md](03-features.md) §3 still hold.

## 1. What the field shows

| Source | What it does best | Lesson for Sprawl |
|---|---|---|
| [Napkin AI](https://dev.to/techsifted/napkin-ai-review-march-2026-turn-text-into-visuals-instantly-97i) | Turns a passage of text into several candidate visuals (flow, mind map, infographic) to choose from | Offer the *kind* of visual that fits the text, not one fixed form; structured text gives better pictures |
| [Eraser](https://www.eraser.io/ai-new) | Diagram-as-code: the AI picks the diagram type, writes the code, and edits by prompt or by code | Sprawl's Mermaid round trip is the right model; widen it to every diagram type |
| [Ilograph](https://www.ilograph.com/docs/editing/walkthroughs/) | One model, several *perspectives*; step-by-step *walkthroughs* that select, highlight and fade parts of a diagram with explainer text | The single biggest gap: Sprawl shows diagrams but cannot *walk someone through* one |
| [IcePanel](https://docs.icepanel.io/core-features/diagramming) / [Structurizr](https://docs.structurizr.com/ui/diagrams/dynamic-view) (C4) | Zoom from system to container to component; *flows* and *dynamic views* tell several stories over the same architecture | Drill-down (a shape opens into its own diagram) and stories layered on one static picture |
| [D2](https://blog.brightcoding.dev/2026/03/09/d2-diagram-language-code-your-visuals-skip-the-drag-and-drop) | Layers (levels of detail) and scenarios (the same diagram in alternate states) | States of a diagram ("normal", "the broker is down") as first-class views |
| [Mermaid diagram types](https://www.drawio.com/docs/manual/mermaid/mermaid-supported-diagram-types/) | Sequence, class, ER, state, Gantt, mind map, timeline, Sankey, quadrant, block, architecture, kanban, treemap, Venn, Wardley | Models already write all of these; Sprawl only accepts flowcharts |
| [Vega-Lite and LLMs](https://arxiv.org/html/2601.15385v1) | Models write declarative chart specs well for common charts; validating against the schema and repairing in code fixes most errors | Data charts as cards from a spec, validated server-side, never generated as pixels |
| [Kumu](https://docs.kumu.io/guides/what-are-loops) | Systems maps: causal loops marked reinforcing or balancing, signed (+/−) links | A causal-loop mode explains dynamics (feedback, delays) that flowcharts cannot |
| [Explorable explanations](https://worrydream.com/ExplorableExplanations/) (Victor, [Case](https://blog.ncase.me/explorable-explanations)) | The reader manipulates the model (sliders, bets, sandboxes) with attention deliberately guided | A sandboxed interactive card (parameters → computed result or chart) is the ceiling of "explaining" |
| [Interactive articles](https://haaao.github.io/papers/interactive-articles) (Distill) | Text and live graphics woven together promote active reading | Walkthrough text belongs beside the diagram, step by step, not in a separate document |
| [MCP Apps](https://www.morphllm.com/mcp-apps) and the [Excalidraw MCP App](https://lobehub.com/ja/mcp/antonpk1-excalidraw-mcp-app) | MCP tools return interactive HTML rendered inline in Claude (`ui://` resources); Excalidraw streams diagrams into the chat with pan, zoom and fullscreen | Show Sprawl's diagrams *in the conversation*, live, not only on the canvas |
| [tldraw agent kit](https://tldraw.dev/starter-kits/agent) | The agent reads both a screenshot and the structured shapes; actions are validated and repaired before applying | Sprawl's structured, validated intents are already the stronger half; a rendered picture would add the other |
| [Flowith](https://www.eesel.ai/blog/flowith-review) / [Miro AI](https://miro.com/blog/whats-new-may-2026/) | AI-readable canvases and agent workflows on the board | Sprawl is already AI-readable; keep pushing semantics, not pixels |
| [Image models 2026](https://news.bgov.com/artificial-intelligence/openai-unveils-image-model-thats-better-at-charts-and-diagrams) | GPT Image 2 and Nano Banana 2 render text-heavy infographics far better, but practitioners still build exact diagrams from code | Use generated images for illustration and atmosphere; keep anything with exact labels as code |
| [Rough.js](https://roughjs.com/) | MIT, under 9 kB: hand-drawn rendering of SVG shapes | A whiteboard style for diagrams at almost no cost |
| [Mayer and dual coding](https://www.sfu.ca/~jcnesbit/EDUC220/ThinkPaper/MayerMoreno2002.pdf) | Words plus pictures beat words alone; related text belongs next to its picture (the split-attention effect). Concept-map meta-analyses exist (Nesbit and Adesope 2006; Schroeder et al. 2017) but their effect sizes were not checked here | Pair every diagram with its explanation, side by side; learner-built maps are worth trying, unproven here |
| [Harvard AI tutor RCT](https://pmc.ncbi.nlm.nih.gov/articles/PMC12179260/) (Kestin et al., 2025) | A tutor designed to make students reason, grounded in worked solutions, roughly doubled learning gains over active learning | A "teach me" mode should question and scaffold, not just draw |

## 2. Ranked plan

Ranked by how much more Claude can explain per unit of work, given what Sprawl already has (server-side layout, Mermaid round trip, semantic intents, MCP).

### V1. Every diagram type (built)

- A **`mermaid` card**: any Mermaid source (sequence, class, ER, state, Gantt, mind map, timeline, Sankey, quadrant, architecture, Wardley, …) rendered by the Mermaid library (MIT) in the browser as SVG. Claude writes what it already knows; nothing to learn.
- Flowcharts stay native, editable shapes (`add_diagram`). `add_diagram` accepts any Mermaid: flowcharts become shapes, everything else a Mermaid card. `get_diagram` returns either.
- Mermaid runs with `securityLevel: "strict"` and no HTML labels, since card text is untrusted.

### V2. Walkthroughs: explain step by step (built)

- A **walkthrough** belongs to a region: ordered steps, each with explainer text (markdown), the items to *highlight* (others fade), and optional camera framing. Ilograph's slide model, IcePanel's flows, and Structurizr's dynamic views in one feature.
- Edges in the current step **animate** (a dash flowing along the path) so a request visibly travels A → B → C.
- Present mode plays walkthroughs; the panel shows the step text beside the diagram (no split attention).
- MCP: `add_walkthrough` / `get_walkthrough`, and the `visualize` prompt adds a walkthrough to every diagram it draws.
- Stored as a new entity in the op log (undoable, replayable), like sketches.

### V3. See it in the chat (built)

- **Server-side SVG rendering** of a region or a walkthrough step: positions already come from the server, so the API can draw shapes, edges and labels itself. That also gives SVG export and image embeds.
- An **MCP App** (`ui://sprawl/region`): `get_diagram` and `add_diagram` return a live, pannable view inline in Claude, with a step-through for walkthroughs and an "Open in Sprawl" link. The diagram appears where the conversation is.
- Check the .NET MCP SDK's MCP Apps support before building; fall back to returning the SVG as an image block.

### V4. Drill-down and perspectives (drill-down and scenarios built)

- **Zoom into a shape**: a shape may link to a *detail* region (its internals). Double-click zooms into it with a breadcrumb back. MCP: `add_diagram` with `detailOf: <cardId>`.
- **The same thing in several diagrams**: a shape may be an *alias* of another card, so "Orders service" in the architecture, the deployment and the checkout flow is one card with one set of notes and backlinks.
- **Scenarios** (D2): alternate states of a diagram, e.g. "broker down", as overlays that restyle and annotate items without copying the diagram.

### V5. Data and systems (tables, charts and causal loops built)

- **Chart cards** from Vega-Lite specs, schema-validated and auto-repaired server-side (the VegaChat approach); data inline or from a CSV-like body. MCP: `add_chart`.
- **Tables / matrices** as a card kind (comparisons, 2×2s), rendered from markdown tables at a readable size.
- **Causal-loop mode** for systems: signed links (+/−), loops labelled reinforcing or balancing, delays marked. Later, a small stock-and-flow simulation.

### V6. Interactive explorables (widgets built)

- A **widget card**: a self-contained HTML/JS explorable (sliders, toggles, a small simulation, a chart that responds) written by Claude, run in a sandboxed iframe (`sandbox="allow-scripts"`, no same-origin, no network, strict CSP), sized like a card. Bret Victor's "reactive document" inside the canvas.
- Parameters can be declared (name, range) so the canvas shows them and the outline tells Claude what the widget does.

### V7. Pictures and style (sketch style and icons built)

- **Image cards** (upload, paste, web image by URL fetched by the browser only) with **annotation hotspots** linked to cards. Generated illustrations through the owner's chosen image model are optional and never used for exact diagrams.
- A **hand-drawn style** per canvas or region (Rough.js) for whiteboard explanations.
- **Icons on shapes** (cloud provider and tech icons) for architecture diagrams.

### V8. Teach mode (built)

- **`teach` prompt**: build a canvas progressively, question by question; withhold the next step until the learner tries ("cognitive gate"); ground in worked examples, as the Harvard tutor did.
- **Quiz on a diagram**: hide labels or edges and ask the learner to restore them (retrieval practice on visuals), feeding the existing question review.
- **Learner-built maps**: Claude checks a map the owner drew (connections missing, wrong direction) through `analyze_canvas`.

## 3. Recommended order

1. **V1 Mermaid cards**: a day's work, instantly every diagram type.
2. **V2 Walkthroughs** with animated edges: the feature that turns pictures into explanations.
3. **V3 Server-side SVG and the MCP App**: explanations appear in the chat itself.
4. **V4 Drill-down and aliases**, then **V5 charts and tables**.
5. **V6 explorables**, **V7 images and style**, **V8 teach mode** as the long tail.

## 4. What was built

- **V1, every diagram type.** Card kind `mermaid`: the body is Mermaid source of any type, rendered in the browser by Mermaid 12.3.0 (MIT, pinned, lazy-loaded, `securityLevel: "strict"`, no HTML labels) and scaled to its card; the owner can resize it. `addDiagram` accepts any type (`Mermaid.DiagramType`): flowcharts stay native shapes, everything else becomes a diagram region holding one rendered card, sized from the source (`MermaidGeometry`: participants and messages for sequences, depth for mind maps, …). `get_diagram` returns the source; `replace` can change the type. Markdown and JSON Canvas exports keep it as a ```` ```mermaid ```` block.
- **V2, walkthroughs.** A `Walkthrough` entity (`wt_…`, migration 0005, in the op log): a region, a title and up to 40 steps of markdown text with the ids to highlight. Names are resolved when written (refs from the batch, ids, or titles inside the region) and unknown names fail with the step and the name. The UI plays it (`W`, the ▶ on the region, or Ctrl+K): the step's items stay lit, the rest fades, connections between lit items animate, the camera frames the step and the text sits in a caption card above the step controls. MCP: `add_walkthrough`, and `add_diagram`'s `walkthrough` (node ids work as highlights), deleting through `reorganize`. The outline lists walkthroughs, and their steps at full depth.
- **V3, in the chat.** `SvgRenderer` draws a region on the server (shapes, cards, groups, orthogonal connections with styles, labels), with every item tagged `data-id` (and `data-group`), colours as CSS variables with light and dark schemes, and all text escaped. `GET …/regions/{region}/svg` (served with `default-src 'none'`) and `…/view` (SVG, Mermaid and walkthroughs). MCP: `show_diagram`, and an MCP App (`ui://sprawl/diagram`, `text/html;profile=mcp-app`) that `show_diagram` and `add_diagram` point to through `_meta.ui.resourceUri`: it renders the SVG inline in Claude with pan, zoom and fit, and steps through walkthroughs with highlighting. It speaks only the documented postMessage JSON-RPC (`ui/initialize`, `ui/notifications/initialized`, `…/tool-result`, `…/size-changed`, `tools/call`) and loads nothing from the network. Checked in a minimal test host; a real check needs Claude with MCP Apps.
- **Not yet:** rendered (non-flowchart) Mermaid cards draw as a titled frame in the server SVG and the chat view, since their drawing needs Mermaid in a browser.
- **Wide content.** Rendered diagrams, tables and charts in a card region span as many masonry columns as they need (at most 3; `ContentGeometry`), and the region widens for them; in a diagram they take their own size. The owner can resize any of them.
- **V5, tables and charts.** Kind `table`: a markdown table sized from its columns and rows. Kind `chart`: a Vega-Lite spec, validated on the server (one JSON object with a mark, layer, concat, facet or repeat; any `url` anywhere is refused, so data is always inline). The browser compiles it to Vega 6.4 and runs it through `vega-interpreter` (`ast: true`), never `new Function`, so it works under the page's CSP. Vega, Vega-Lite and the interpreter are BSD-3, pinned, lazy-loaded.
- **V4, drill-down.** A card may name a `detail` region (migration 0006). `add_diagram detailOf=<card>` makes a diagram the inside of a shape; the card shows a zoom badge, double-click zooms into the detail, and the detail region shows a back link to the card. `update_cards detail` sets or clears it (`none`). The outline marks `[details: rg_…]`; deleting the detail region drops the link.
- **V6, widgets.** Kind `widget`: an HTML fragment (or whole document) with inline CSS and JS, up to the code-body limit, sized from a leading `<!-- size: WxH -->` (else 576 × 400). `GET …/cards/{card}/widget` serves it to the owner only, as its own document with a policy that allows inline script and style and nothing else (no fetch, connect, frames, forms or workers; `frame-ancestors 'self'`). The UI frames it with `sandbox="allow-scripts"` and no same-origin, so it runs in an opaque origin with no access to Sprawl's cookies, storage or API. Fragments get Sprawl's colours as CSS variables (`--bg`, `--surface`, `--text`, `--muted`, `--border`, `--accent`) in light and dark.
- **V7, sketch style.** A per-browser view preference (Ctrl+K "Sketch style"): an SVG turbulence filter wobbles shapes, cards, regions and lines, and labels take a handwritten face. No new dependency; the stored canvas is unchanged.
- **V8, teach and quiz.** MCP prompts `teach_me` (build the canvas one step at a time, asking before each next step, with worked examples and a walkthrough) and `quiz_me` (questions from a region, answers checked against the canvas, misses recorded as question cards). In the UI, `Q` (or Ctrl+K) quizzes the selected region: labels of its shapes and cards and its connection labels are hidden; a click reveals one, with a count, Reveal all, Hide all and Esc.
- **V5, causal loops.** Region layout `loops`. A connection's label carries its polarity (`+`, or `-`/`−`; `s` and `o` also read) and the word "delay" or `||` marks a delay. `CausalLoops.Find` lists the elementary cycles among the region's members (at most 24, up to 12 long, each found once from its lowest member) and names them R1, R2 (even number of − links: reinforcing), B1 (odd: balancing) or L1 (an unsigned link), longest first. The layout puts the members on a circle, clockwise from the top in loop order. Links curve to the left of their direction, so they bow outward and the two links of a two-variable loop never overlap. The app, the server SVG and the chat view draw a badge per loop at the middle of its members, with an arrow turning the way the loop runs. The outline lists every loop for Claude. `add_diagram layout=loops` draws one from a Mermaid flowchart (`A -->|+| B`, `B -->|- delay| A`). The web's `loops.ts` mirrors the C# and the two are tested on the same diagram.
- **V4, scenarios.** A walkthrough step may carry `marks`: an item, a state (`down`, `slow`, `hot`, `added`, `removed`, `changed`) and an optional note of up to 80 characters. Marks are named like highlights and resolved when written. During the step a marked item is lit, restyled for its state and tagged with the state and note; a marked connection takes the note as its label. So one diagram tells "normal", "the broker is down" and "the retry storm" without copies. They work in `add_walkthrough` and `add_diagram`'s walkthrough (Mermaid node ids), and in the chat view. The outline shows them as `{cd_… down "503s"}`. Steps are stored as JSON, so no migration was needed.
- **V7, icons.** A card or shape may carry one of 70 icons (`cards.icon`, migration 0007): user, server, database, cache, queue, cloud, api, function, container, globe, phone, lock, shield, mail, load-balancer, bot, brain and more. The drawings are Lucide's (ISC, lucide-static 1.69.0). `scripts/generate-icons.mjs` writes them once for the web (`canvas/icons.ts`), the server (`IconPaths.g.cs`), the vocabulary (`Icons.g.cs`) and the MCP enum, with Lucide's licence in each file. There is no runtime dependency. An icon sits above a shape's label, and the shape grows a row to hold it; on a plain card it replaces the kind glyph. In Mermaid, `id@{ shape: cyl, label: "Orders DB", icon: "database" }` sets one. `fa:`/`mdi:` prefixes and common names (postgres, redis, kafka, k8s, lambda, email) are understood, and unknown icons are dropped rather than refused. `get_diagram` writes icons back. The card panel has an icon picker; MCP takes `icon` on new cards and in `update_cards` (`none` clears it).
- **V8, map checks.** The `check_my_map` prompt has Claude review a concept map, flowchart or causal-loop diagram the owner drew. It says what is right first, then asks a leading question card for each missing idea, wrong-way link or wrong sign, in a "Map feedback" region. It never edits the owner's map. Only on request does it draw a corrected copy beside it, with a walkthrough that marks what changed.
