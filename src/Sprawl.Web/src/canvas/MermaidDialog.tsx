import { useState } from "react";

export interface MermaidDraft {
  title: string;
  mermaid: string;
  /** Redraw the diagram with this title in place. */
  replace: boolean;
}

const EXAMPLE = `flowchart LR
  user@{ shape: person, label: "Customer" } --> web[Web app]
  web -->|HTTPS| api{{API gateway}}
  api ==> orders[Orders service]
  orders --> db[(Orders DB)]
  orders -.->|OrderPlaced| bus@{ shape: h-cyl, label: "Event bus" }
  bus -.-> email[Email worker]`;

/**
 * Draw a diagram from Mermaid, or edit one as text: the fastest way to put a whole architecture or flow on the canvas,
 * and the same text Claude reads and writes through the MCP.
 */
export function MermaidDialog({ initial, onSubmit, onClose }: { initial?: MermaidDraft; onSubmit: (draft: MermaidDraft) => void; onClose: () => void }) {
  const [title, setTitle] = useState(initial?.title ?? "");
  const [mermaid, setMermaid] = useState(initial?.mermaid ?? "");
  const editing = initial?.replace ?? false;
  const submit = () => { if (title.trim() && mermaid.trim()) onSubmit({ title: title.trim(), mermaid, replace: editing }); };
  return (
    <div className="overlay" onPointerDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <form className="sheet mermaid-sheet" role="dialog" aria-modal="true" aria-label={editing ? "Edit diagram" : "New diagram"}
        onSubmit={(e) => { e.preventDefault(); submit(); }}
        onKeyDown={(e) => { if (e.key === "Escape") { e.preventDefault(); onClose(); } if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) { e.preventDefault(); submit(); } }}>
        <header className="panel-header">
          <span className="panel-title">{editing ? "Edit diagram as Mermaid" : "New diagram from Mermaid"}</span>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
        </header>
        <div className="panel-body">
          <label className="field">
            <span>Title</span>
            <input autoFocus={!editing} value={title} maxLength={80} placeholder="Checkout architecture" readOnly={editing} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label className="field">
            <span>Flowchart <em>(flowchart TD or LR · [box] (rounded) {"{decision}"} [(database)] ((event)) · A --&gt; B, A -.-&gt;|async| B, A ==&gt; B · subgraph … end)</em></span>
            <textarea className="is-code" rows={14} value={mermaid} autoFocus={editing} spellCheck={false} placeholder={EXAMPLE} onChange={(e) => setMermaid(e.target.value)} />
          </label>
          <footer className="dialog-actions">
            {!mermaid && <button type="button" className="quiet small" onClick={() => { setMermaid(EXAMPLE); if (!title) setTitle("Example architecture"); }}>Use the example</button>}
            <span className="dialog-hint"><kbd>Ctrl</kbd><kbd>↵</kbd></span>
            <button type="submit" className="primary" disabled={!title.trim() || !mermaid.trim()}>{editing ? "Redraw" : "Draw"}</button>
          </footer>
        </div>
      </form>
    </div>
  );
}
