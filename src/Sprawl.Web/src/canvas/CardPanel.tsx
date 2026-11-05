import { useEffect, useRef, useState } from "react";
import type { Card, CardKind, CanvasDocument, Connection, ConnectionKind, Ends, Intent, LineStyle } from "../api/types";
import { SHAPES, ShapeIcon } from "./shapes";
import { CardLinks } from "./CardLinks";
import { Body, KindGlyph } from "./parts";

const KINDS: CardKind[] = ["idea", "note", "question", "link", "code"];
const CONNECTION_KINDS: ConnectionKind[] = ["relates", "leads-to", "contradicts", "example-of", "depends-on"];
const LINES: LineStyle[] = ["solid", "dashed", "dotted", "thick"];
const ENDS: [Ends, string][] = [["end", "→"], ["start", "←"], ["both", "↔"], ["none", "—"]];

interface Props {
  doc: CanvasDocument;
  cardId: string;
  write: (intents: Intent[], touched: string[]) => Promise<unknown>;
  onClose: () => void;
  onFocus: (id: string) => void;
  onTag: (tag: string) => void;
}

/** Read and edit one card. Fields save when they lose focus (or Ctrl+Enter); kind and emphasis save immediately. */
export function CardPanel({ doc, cardId, write, onClose, onFocus, onTag }: Props) {
  const card = doc.cards.find((c) => c.id === cardId);
  if (!card) {
    return (
      <aside className="panel" aria-label="Card">
        <header className="panel-header"><span className="panel-title">Card</span><button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button></header>
        <p className="panel-empty">This card is no longer on the canvas.</p>
      </aside>
    );
  }
  return <CardEditor key={card.id} doc={doc} card={card} write={write} onClose={onClose} onFocus={onFocus} onTag={onTag} />;
}

function CardEditor({ doc, card, write, onClose, onFocus, onTag }: Omit<Props, "cardId"> & { card: Card }) {
  const [title, setTitle] = useState(card.title);
  const [body, setBody] = useState(card.body);
  const [url, setUrl] = useState(card.url ?? "");
  const [editingBody, setEditingBody] = useState(false);
  const focused = useRef<string | null>(null);

  // Claude (or another tab) may edit this card while it is open: take its changes for any field not being typed in.
  useEffect(() => {
    if (focused.current !== "title") setTitle(card.title);
    if (focused.current !== "body") setBody(card.body);
    if (focused.current !== "url") setUrl(card.url ?? "");
  }, [card.title, card.body, card.url, card.updatedAt]);

  const update = (fields: Omit<Extract<Intent, { type: "updateCard" }>, "type" | "id">) => void write([{ type: "updateCard", id: card.id, ...fields }], [card.id]);
  const saveTitle = () => { focused.current = null; if (title.trim() && title.trim() !== card.title) update({ title: title.trim() }); else setTitle(card.title); };
  const saveBody = () => { focused.current = null; setEditingBody(false); if (body !== card.body) update({ body }); };
  const saveUrl = () => { focused.current = null; if (url.trim() !== (card.url ?? "")) update({ url: url.trim() }); };

  const connections = doc.connections.filter((c) => c.sourceId === card.id || c.targetId === card.id);
  const region = doc.regions.find((r) => r.id === card.regionId);

  return (
    <aside className="panel" aria-label="Card">
      <header className="panel-header">
        <span className="panel-title"><KindGlyph kind={card.kind} /> {region ? region.title : "Loose card"}</span>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
      </header>
      <div className="panel-body">
        <textarea
          className="panel-card-title" rows={2} value={title} maxLength={120} aria-label="Title"
          onFocus={() => { focused.current = "title"; }} onChange={(e) => setTitle(e.target.value.replace(/\n/g, " "))} onBlur={saveTitle}
          onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); e.currentTarget.blur(); } }}
        />
        <div className="segmented" role="radiogroup" aria-label="Kind">
          {KINDS.map((kind) => (
            <button key={kind} type="button" role="radio" aria-checked={card.kind === kind} className={card.kind === kind ? "is-on" : ""}
              disabled={kind === "link" && !card.url && !url.trim()} title={kind === "link" && !card.url ? "Add a URL first" : kind}
              onClick={() => { if (card.kind !== kind) update(kind === "link" ? { kind, url: url.trim() } : { kind }); }}>
              <KindGlyph kind={kind} /><span>{kind}</span>
            </button>
          ))}
        </div>
        <div className="field">
          <span>Shape</span>
          <div className="shape-picker" role="radiogroup" aria-label="Shape">
            <button type="button" role="radio" aria-checked={!card.shape} className={!card.shape ? "is-on" : ""} title="A card, not a shape"
              onClick={() => { if (card.shape) update({ shape: "none" }); }}>
              <svg className="shape-icon" viewBox="0 0 24 18" width="24" height="18" aria-hidden="true"><path d="M3 3h18v12H3z M6 7h9 M6 10h6" fill="none" stroke="currentColor" strokeWidth="1.3" /></svg>
            </button>
            {SHAPES.map(({ shape, label }) => (
              <button key={shape} type="button" role="radio" aria-checked={card.shape === shape} className={card.shape === shape ? "is-on" : ""} title={label} aria-label={label}
                onClick={() => { if (card.shape !== shape) update({ shape }); }}>
                <ShapeIcon shape={shape} />
              </button>
            ))}
          </div>
        </div>
        {(card.kind === "link" || !card.shape) && <label className="field">
          <span>URL</span>
          <input value={url} placeholder="https://…" onFocus={() => { focused.current = "url"; }} onChange={(e) => setUrl(e.target.value)} onBlur={saveUrl} />
        </label>}
        <div className="field">
          <span>{card.kind === "code" ? `Code${card.language ? ` · ${card.language}` : ""}` : "Notes"} <em>(markdown · [[Card]] links · #tags)</em></span>
          {editingBody || card.body.length === 0 ? (
            <textarea
              className={card.kind === "code" ? "is-code" : ""} rows={10} value={body} autoFocus={editingBody} aria-label="Body"
              onFocus={() => { focused.current = "body"; setEditingBody(true); }} onChange={(e) => setBody(e.target.value)} onBlur={saveBody}
              onKeyDown={(e) => { if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) e.currentTarget.blur(); if (e.key === "Escape") { setBody(card.body); e.currentTarget.blur(); } }}
            />
          ) : (
            <div className="panel-preview" role="button" tabIndex={0} onClick={() => setEditingBody(true)} onKeyDown={(e) => { if (e.key === "Enter") setEditingBody(true); }} title="Click to edit">
              {card.kind === "code" ? <pre className="card-code"><code>{card.body}</code></pre> : <Body markdown={card.body} />}
            </div>
          )}
        </div>
        <label className="toggle">
          <input type="checkbox" checked={card.emphasis === "key"} onChange={(e) => update({ emphasis: e.target.checked ? "key" : "normal" })} />
          <span>Key idea</span>
        </label>
        {connections.length > 0 && (
          <section className="panel-section">
            <h4>Connections</h4>
            <ul className="connections">
              {connections.map((c) => <ConnectionRow key={c.id} doc={doc} cardId={card.id} connection={c} write={write} onFocus={onFocus} />)}
            </ul>
          </section>
        )}
        <CardLinks doc={doc} card={card} write={write} onFocus={onFocus} onTag={onTag} />
        <footer className="panel-footer">
          <span className="panel-meta">{card.createdBy === "ai" ? "Added by Claude" : "Added by you"} · {new Date(card.createdAt).toLocaleDateString()}</span>
          <button type="button" className="danger" onClick={() => { void write([{ type: "deleteCards", ids: [card.id] }], [card.id]); onClose(); }}>Delete</button>
        </footer>
      </div>
    </aside>
  );
}

function ConnectionRow({ doc, cardId, connection, write, onFocus }: { doc: CanvasDocument; cardId: string; connection: Connection; write: Props["write"]; onFocus: (id: string) => void }) {
  const outgoing = connection.sourceId === cardId;
  const otherId = outgoing ? connection.targetId : connection.sourceId;
  const other = doc.cards.find((c) => c.id === otherId)?.title ?? doc.regions.find((r) => r.id === otherId)?.title ?? otherId;
  const restyle = (fields: { kind?: ConnectionKind; line?: LineStyle; ends?: Ends; label?: string }) =>
    void write([{ type: "updateConnection", id: connection.id, ...fields }], [connection.id]);
  const ends: Ends = connection.ends ?? (["leads-to", "depends-on", "example-of"].includes(connection.kind) ? "end" : "none");
  return (
    <li>
      <span className="connection-dir" aria-label={outgoing ? "to" : "from"}>{outgoing ? "→" : "←"}</span>
      <button type="button" className="link-button" onClick={() => onFocus(otherId)}>{other}</button>
      <select value={connection.kind} onChange={(e) => restyle({ kind: e.target.value as ConnectionKind })} aria-label="Connection kind">
        {CONNECTION_KINDS.map((kind) => <option key={kind} value={kind}>{kind}</option>)}
      </select>
      <span className="connection-style">
        <select value={connection.line ?? "solid"} onChange={(e) => restyle({ line: e.target.value as LineStyle })} aria-label="Line style" title="Line">
          {LINES.map((line) => <option key={line} value={line}>{line}</option>)}
        </select>
        <select value={ends} onChange={(e) => restyle({ ends: e.target.value as Ends })} aria-label="Arrowheads" title="Arrowheads">
          {ENDS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select>
        <input className="connection-label" defaultValue={connection.label ?? ""} key={connection.label ?? ""} placeholder="label" maxLength={40} aria-label="Label"
          onBlur={(e) => { const label = e.target.value.trim(); if (label !== (connection.label ?? "")) restyle({ label }); }}
          onKeyDown={(e) => { if (e.key === "Enter") e.currentTarget.blur(); }} />
      </span>
      <button type="button" className="icon-button" aria-label="Remove connection" onClick={() => void write([{ type: "disconnect", id: connection.id }], [connection.id])}>×</button>
    </li>
  );
}
