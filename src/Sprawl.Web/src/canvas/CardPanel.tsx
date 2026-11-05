import { useEffect, useRef, useState } from "react";
import type { Card, CardKind, CanvasDocument, Connection, ConnectionKind, Intent } from "../api/types";
import { CardLinks } from "./CardLinks";
import { Body, KindGlyph } from "./parts";

const KINDS: CardKind[] = ["idea", "note", "question", "link", "code"];
const CONNECTION_KINDS: ConnectionKind[] = ["relates", "leads-to", "contradicts", "example-of", "depends-on"];

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
        <label className="field">
          <span>URL</span>
          <input value={url} placeholder="https://…" onFocus={() => { focused.current = "url"; }} onChange={(e) => setUrl(e.target.value)} onBlur={saveUrl} />
        </label>
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
  const changeKind = (kind: ConnectionKind) => void write([
    { type: "disconnect", id: connection.id },
    { type: "connect", from: connection.sourceId, to: connection.targetId, kind, ...(connection.label ? { label: connection.label } : {}) },
  ], [connection.id]);
  return (
    <li>
      <span className="connection-dir" aria-label={outgoing ? "to" : "from"}>{outgoing ? "→" : "←"}</span>
      <button type="button" className="link-button" onClick={() => onFocus(otherId)}>{other}</button>
      <select value={connection.kind} onChange={(e) => changeKind(e.target.value as ConnectionKind)} aria-label="Connection kind">
        {CONNECTION_KINDS.map((kind) => <option key={kind} value={kind}>{kind}</option>)}
      </select>
      <button type="button" className="icon-button" aria-label="Remove connection" onClick={() => void write([{ type: "disconnect", id: connection.id }], [connection.id])}>×</button>
    </li>
  );
}
