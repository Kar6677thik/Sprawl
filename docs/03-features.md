# Sprawl feature plan: the best of comparable tools

Written 5 November 2026. **Status: P1–P5 built, except PNG export (P2) and ink (P5), both deferred on purpose.** This plan surveys tools that do part of what Sprawl does, keeps the ideas that suit a canvas that Claude mostly writes, and ranks them. Fixed decisions in [01-design.md](01-design.md) still hold: one owner, the MCP never sees geometry, and every change is a batch in the op log.

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

### P2: Output and presenting (built, except PNG)

- **Export** (`GET /api/v1/canvases/{id}/export`, palette): Markdown (regions as headings, `[[links]]` kept, so it drops into Obsidian) and JSON Canvas 1.0 (absolute geometry, regions as groups, tints as preset colours, card kinds in a `sprawlKind` extension). "Copy canvas as Markdown" puts it on the clipboard for pasting into a chat. `Sprawl.Domain/Exchange.cs`.
- **Import** (`POST /api/v1/canvases/import`, owner only, "Import…" on the home page): groups become regions (one level of nesting; deeper groups flatten), each node joins the smallest group containing its centre, loose nodes go to *Unsorted*, the first line of text is the title (`?` makes a question, a lone fenced block makes code), link and file nodes become link and note cards, and edges become connections (a label naming a kind sets it). It is one create batch, so placement is Sprawl's own; Tidy afterwards if wanted.
- **Present** (`P`): top-level regions in reading order, framed one per step; arrows, Space, Home/End, Esc.
- **Not yet: PNG export.** It needs a DOM-to-image dependency; deferred until it is wanted.

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

### P5: Freeform and time (built, except ink)

- **History replay** (`Shift+H`): a slider over every version, Space to play, arrows to step, Restore to revert to the version shown (a new version; nothing is lost). Past states come from `GET /state?version=`, are cached in the session, and are read-only: edits, drags and the palette are off while replaying.
- **Saved searches:** any palette search (for example `#risk question`) can be saved; saved ones appear under Ctrl+K and rerun with one keystroke. They are kept per canvas in the browser's local storage, because they are a view preference, not canvas content.
- **Not built: ink and shapes.** They would need a new entity type through the op log, undo and placement, and Claude could not read them; they add little to a canvas that Claude mostly writes. Revisit if hand sketching becomes a real need.
