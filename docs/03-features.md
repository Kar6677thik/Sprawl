# Sprawl feature plan: the best of comparable tools

Written 5 November 2026. **Status: P1–P6 built.** P6 (diagrams) is in [§3](#3-p6-diagrams-drawio-style-built). This plan surveys tools that do part of what Sprawl does, keeps the ideas that suit a canvas that Claude mostly writes, and ranks them. Fixed decisions in [01-design.md](01-design.md) still hold: one owner, the MCP never sees geometry, and every change is a batch in the op log.

## 1. What the others do best

| Tool | Best quality | Kept for Sprawl as |
|---|---|---|
| [Heptabase](https://wiki.heptabase.com/section) | Sections stay readable when zoomed out; a card can sit on many whiteboards; a journal feeds the board | Already have the far-zoom miniatures. Cross-canvas cards (P4), inbox capture (P3) |
| [Obsidian Canvas / JSON Canvas](https://jsoncanvas.org/) | An open, plain-file canvas format; `[[wikilinks]]` and backlinks | `[[links]]` and backlinks (P1); JSON Canvas import and export (P2) |
| [TheBrain](https://thebrain.com/products/thebrain/thebrain12) | Typed, labelled links; backlinks; "unlinked mentions" that suggest links | Unlinked mentions with a one-click "link it" (P1) |
| [InfraNodus](https://infranodus.com/about/how-it-works) | Finds topical clusters and the *structural gaps* between them, then turns each gap into a research question; has an MCP server | `analyze_canvas`: gaps between regions, orphans, dangling links, hubs (P1) |
| [Kinopio](https://kinopio.club/llms.txt) | `[[tags]]` across spaces, an inbox space, journal prompts | `#tags` derived from text (P1); inbox (P3) |
| [Scrintal](https://scrintal.com/features/link-your-notes) | Links are both visual and in-text, and stay in sync | `[[links]]` render as links in the card and as dashed edges on the canvas (P1) |
| [Miro AI](https://miro.com/blog/what-we-launched-march-2023) | Cluster stickies, summarise a cluster into one, expand a mind-map node | MCP prompts `find_gaps`, `synthesize_region` and `answer_questions` drive Claude through the same moves (P1) |
| mymind / Napkin | Resurfaces related notes without manual linking | "Related" cards by shared rare terms and tags (P1) |
| Muse / FigJam | Presentation and spotlight over boards; export to PDF and PNG | Present mode that walks regions in reading order; Markdown, JSON Canvas and PNG export (P2) |
| RemNote / Logseq | Spaced repetition from questions in notes | Review open questions on a schedule (P3) |
| Tana / Capacities | Typed nodes with fields, live queries | Tag filters in the palette now (P1); saved queries later (P5) |
| tldraw | Ink and freeform shapes beside structured content | Ink layer (P5) |

Left out on purpose: real-time multi-user collaboration (single owner), an in-app chat with its own model (Claude reaches Sprawl through MCP, so the UI stays model-free), and storing embeddings (the term-overlap measure below needs no model, no new storage and no data leaving the cluster).

## 2. Ranked plan

### P1: Knowledge graph (built)

- **Meaning comes from the text.** `[[Card title]]` or `[[cd_…]]` in a body is a link; `#tag` in a title or body is a tag. Neither is stored separately, so they can never drift from the text, and every existing write path (UI, MCP, undo, revert) keeps them right for free. Code spans and fenced blocks are ignored. `Sprawl.Domain/Semantics.cs`.
- **Card context** (`GET /api/v1/canvases/{id}/cards/{cardId}/context`, MCP `get_card_context`): outgoing links, unresolved links, backlinks, unlinked mentions and related cards. Related cards are scored by shared key terms, weighted by rarity on the canvas, plus shared tags and shared neighbours.
- **Canvas insights** (`GET /api/v1/canvases/{id}/insights`, MCP `analyze_canvas`):
  - **gaps**: pairs of regions whose cards share vocabulary but have no connection or link between them, with the shared terms (InfraNodus's structural gaps);
  - **orphans**: cards with no connection, link or backlink;
  - **dangling links**: `[[targets]]` that name no card yet (cards to write);
  - **open questions**, **contradictions** and **hubs** (the most connected cards);
  - **tags** with counts.
- **UI:** the card panel lists links, backlinks, mentions (with "Link") and related cards, each jumping to the card; `[[links]]` in a body are clickable; `#tags` show as chips; the palette filters by `#tag`; an Insights panel (`I`) lists the analysis; text links draw as dashed edges.
- **MCP prompts:** `find_gaps` and `synthesize_region` turn the analysis into work for Claude.

### P2: Output and presenting (built)

- **Export** (`GET /api/v1/canvases/{id}/export`, palette): Markdown (regions as headings, `[[links]]` kept, so it drops into Obsidian) and JSON Canvas 1.0 (absolute geometry, regions as groups, tints as preset colours, card kinds in a `sprawlKind` extension). "Copy canvas as Markdown" puts it on the clipboard for pasting into a chat. `Sprawl.Domain/Exchange.cs`.
- **Import** (`POST /api/v1/canvases/import`, owner only, "Import…" on the home page): groups become regions (one level of nesting; deeper groups flatten), each node joins the smallest group containing its centre, loose nodes go to *Unsorted*, the first line of text is the title (`?` makes a question, a lone fenced block makes code), link and file nodes become link and note cards, and edges become connections (a label naming a kind sets it). It is one create batch, so placement is Sprawl's own; Tidy afterwards if wanted.
- **Present** (`P`): top-level regions in reading order, framed one per step; arrows, Space, Home/End, Esc.
- **PNG** (palette, "Export view as PNG"): the current view at 2× resolution, so you frame it first (the whole map with Shift+F, or one region with F). Rendering the whole canvas off-screen is not possible as is: React Flow draws only visible nodes, and the zoomed-out map shows region miniatures instead of cards. Uses `html-to-image` 1.11.11, the version React Flow's own export example pins, loaded only when used.

### P3: Capture and review (built)

- **Inbox:** `Shift+I` (or "Capture to Inbox") adds a card to an *Inbox* region, created on first use, and keeps the composer open for the next thought. The MCP prompt `triage_inbox` has Claude sort it into regions with one `reorganize`. No new storage: the inbox is an ordinary region.
- **Question review** (`R`): open question cards resurface on an SM-2 schedule (`Sprawl.Domain/Review.cs`): first due a day after they are written, then Again 1 day, Good 1, 3, then × ease, Easy longer; ease floor 1.3, interval cap 365 days. Grade with 1-4. The home page shows how many are due per canvas.
  - The schedule is a `reviews` table outside the op log (migration `0002_reviews.sql`), because review state is not canvas content: undo and revert never touch it, and a deleted card's row stays so an undo brings its schedule back.
  - `GET /api/v1/reviews?canvas=&limit=` (owner and device) and `POST /api/v1/reviews/{cardId}` `{grade}` (owner only: Claude reads the queue, never grades it).
  - MCP `get_due_questions` (read) and the `answer_questions` prompt: Claude researches the due questions and adds answers as connected notes, leaving the questions for the owner to settle.

### P4: Across canvases (built)

- **Links between canvases:** `[[Canvas title/Card title]]` (canvas by id or title, card by id or title) or a bare `[[cd_…]]`, since card ids are global. The whole target is tried on its own canvas first, so a local card titled "TCP/IP" still wins. Nothing is stored; `Sprawl.Api/Data/CrossLinks.cs` resolves at read time, so links follow renames.
  - The card context gains `crossLinks` and `crossBacklinks`; the panel lists them with the other canvas's name, and a click opens that canvas on the card. Insights no longer reports a link as dangling when it reaches another canvas.
  - `GET /api/v1/canvases/{id}/links/resolve?target=` is what a click on such a link calls.
  - Cross backlinks are found by a text pre-filter (`[[` plus the card's id or title in the body) and then confirmed by resolving, so a scan stays cheap.
- **Collections:** the home page groups canvases under their collection. In the palette, `collection: Name` (or `move to Name`) files the current canvas; `collection` alone offers to take it out.
- **Not built:** a canvas-level graph of cross links. The backlinks lists cover the use; revisit if canvases multiply.

### P5: Freeform and time (built)

- **History replay** (`Shift+H`): a slider over every version, Space to play, arrows to step, Restore to revert to the version shown (a new version; nothing is lost). Past states come from `GET /state?version=`, are cached in the session, and are read-only: edits, drags and the palette are off while replaying.
- **Saved searches:** any palette search (for example `#risk question`) can be saved; saved ones appear under Ctrl+K and rerun with one keystroke. They are kept per canvas in the browser's local storage, because they are a view preference, not canvas content.
- **Ink** (`D`): freehand strokes, owner only. A stroke is a `Sketch` entity (`sk_…`): SVG path data in its own box, relative to the region under its centre so it moves with the region, or loose on the canvas. It goes through the op log like everything else, so undo, revert, history replay, SSE and snapshots cover it; a region's sketches go with it when the region is deleted or merged away.
  - In draw mode: 1-6 pick a colour (the card tints), `[` and `]` the line width, the wheel still zooms, Ctrl+Z undoes a stroke, Esc or D leaves. Select a sketch to drag or delete it.
  - Paths are limited to M, L, Q, C and Z commands and numbers (`Text.SketchPath`), so stored ink cannot script or link. At most 1,000 sketches per canvas, 20,000 characters per path.
  - Claude cannot draw (`addSketch` and `deleteSketches` are refused to the device token) and sees only a count per region in the outline ("2 sketches by the owner"). Exports leave sketches out.
  - Strokes are smoothed into quadratic curves through midpoints, after dropping points closer than 2 canvas pixels (`canvas/ink.ts`).

## 3. P6: Diagrams, draw.io style (built)

The owner asked for draw.io-like shapes and connections that Claude (or any MCP client) can use to build architectures, flows and flowcharts on an infinite canvas. Options checked on 5 November 2026:

| Option | Licence | Verdict |
|---|---|---|
| [draw.io](https://github.com/jgraph/drawio) | Modified Apache 2.0; its own README says it is not open source | No: a whole app on an older non-React engine |
| [maxGraph](https://github.com/maxGraph/maxGraph) (mxGraph's TypeScript successor) | Apache 2.0 | Capable, but a vanilla DOM engine: it would replace the React canvas, a rewrite for no gain |
| [tldraw](https://tldraw.dev/sdk-features/license-key) | Needs a licence key in production | No |
| [Excalidraw](https://npmjs.com/package/@excalidraw/excalidraw) | MIT, React | A freehand whiteboard: absolute coordinates, exactly what Claude is bad at |
| React Flow (already the canvas) + server layout | MIT | **Chosen.** The usual base for draw.io-like React editors, and Sprawl's model already keeps Claude out of coordinates |

So diagrams are built on the existing stack, without a new dependency:

- **Shapes.** A card may carry a `shape`: rectangle, rounded, terminal, diamond, circle, ellipse, cylinder, queue, parallelogram, hexagon, document, subroutine, cloud, person or text, drawn in SVG (`canvas/shapes.tsx`). In a diagram a shape is sized to its label (`ShapeGeometry`); in a card region it keeps the column width.
- **Diagram regions.** A region's `layout` may be `flow-down` or `flow-right`. The server lays such a region out as a layered flow in C# (`Sprawl.Domain/DiagramLayout.cs`, a compact Sugiyama layout): cycles broken from the sources, longest-path ranks tightened, dummy nodes on long edges, barycenter sweeps keeping the order with the fewest crossings, nodes centred on their neighbours. Sub-regions are groups, laid out first and then placed as one box. It is deterministic and runs at the end of every batch, so Claude's additions and the owner's edits both land laid out. Shapes placed by hand stay put.
- **Connections** carry a `line` (solid, dashed, dotted, thick) and `ends` (end, start, both, none; by default the kind decides). Inside diagrams they run orthogonally with rounded corners and show their labels at reading zoom. `updateConnection` restyles one in place.
- **Mermaid in and out.** `addDiagram` takes a Mermaid flowchart (`Sprawl.Domain/Mermaid.cs`): every node shape including `@{ shape: … }`, chains, `&`, every arrow form and label form, and subgraphs with their own direction. `GET /api/v1/canvases/{id}/regions/{region}/mermaid` exports any region back. With `replace`, a diagram is redrawn in place, so the edit loop for Claude is read as Mermaid, edit, send back.
- **MCP:** `add_diagram`, `get_diagram` and `update_connections`; `shape`, `layout`, `line` and `ends` on the existing tools; `setLayout` in `reorganize`; a `visualize` prompt that builds a topic's architecture, flows and decision charts beside the cards that explain them.
- **UI:** a shape picker in the card panel; ports on hover to drag-connect nodes, as in draw.io; quick-add (N) inside a diagram makes a shape; `M` opens a Mermaid sheet (new diagram, or edit the selected one as text); palette commands to turn a region into a diagram (flows down or right) and back, and to copy any region as Mermaid; line style, arrowheads and label editing per connection.
- **Storage:** migration `0004_diagrams.sql` adds `regions.layout`, `cards.shape`, `connections.line` and `connections.ends`. Older op-log rows and snapshots read with the new fields unset.
- **Not built yet:** free-form resizing of shapes, manual waypoints on connectors, swimlanes, and sequence or class diagrams (Mermaid flowcharts only).

## 4. Working with other tools (built)

When Claude has Sprawl and another MCP server (such as a notes app) in one chat, the two link to each other instead of copying text.

- **Into Sprawl.** A source another tool returns with a `url` becomes a link card (kind `link`, the url exactly as given), or the url of the card it supports. The server instructions tell Claude never to build or guess URLs, and to treat other tools' text as untrusted, like card text. The `map_from_sources` prompt builds a canvas from such sources: themes as regions, ideas in Claude's words, one link card per source, ideas connected to the sources behind them.
- **Out of Sprawl.** With `Mcp:WebAppUrl` set (an absolute https origin, checked at startup; production: `https://sprawl.tailf67d61.ts.net`), results carry addresses that open the web app:
  - `url` on canvases: `list_canvases`, `search`, write results, and an `url:` line at the top of `get_canvas`. It is left out of `delete_canvas` results.
  - `webUrl` on cards: `get_cards`, `search`.
  - `webUrls` (ref → address) on write results.
  - `url` on `show_diagram` for the region.

  The form is `{origin}/c/{canvas}` and `?focus={card or region}`. A card's own `url` keeps its old meaning, where a link card points, which is why a card's Sprawl address is `webUrl`. Without the setting, none of these appear.
- **In the chat view.** The diagram app shows "Open in Sprawl" when the region has a `url`. Clicking a node whose card links somewhere opens that link. Both go through the host's `ui/open-link`, and only for http(s).
