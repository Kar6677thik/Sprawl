import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { Card, CardContext, CardRef, CanvasDocument, ExternalCardRef, Intent } from "../api/types";
import { navigate } from "../router";
import { KindGlyph } from "./parts";
import { linkFirstMention } from "./semantics";

interface Props {
  doc: CanvasDocument;
  card: Card;
  write: (intents: Intent[], touched: string[]) => Promise<unknown>;
  onFocus: (id: string) => void;
  onTag: (tag: string) => void;
}

/**
 * The card's place in the canvas's web: tags, its `[[links]]`, backlinks, unlinked mentions (one click links them) and
 * related cards it is not yet connected to (one click connects them). Refetched whenever the canvas version changes.
 */
export function CardLinks({ doc, card, write, onFocus, onTag }: Props) {
  const [context, setContext] = useState<CardContext | null>(null);
  useEffect(() => {
    let live = true;
    const timer = window.setTimeout(() => api.cardContext(doc.meta.id, card.id).then((c) => { if (live) setContext(c); }, () => { if (live) setContext(null); }), 120);
    return () => { live = false; window.clearTimeout(timer); };
  }, [doc.meta.id, card.id, doc.version]);
  if (!context || context.card.id !== card.id) return null;

  const createMissing = (title: string) => void write([{ type: "addCard", kind: "note", title, ...(card.regionId ? { region: card.regionId } : {}) }], []);
  const linkMention = (from: CardRef) => {
    const source = doc.cards.find((c) => c.id === from.id);
    if (source) void write([{ type: "updateCard", id: source.id, body: linkFirstMention(source.body, card.title) }], [source.id]);
  };
  const connect = (to: string) => void write([{ type: "connect", from: card.id, to, kind: "relates" }], [card.id, to]);
  const empty = context.tags.length + context.links.length + context.unresolvedLinks.length + context.backlinks.length + context.mentions.length + context.related.length
    + context.crossLinks.length + context.crossBacklinks.length === 0;
  if (empty) return null;

  return (
    <>
      {(context.links.length > 0 || context.unresolvedLinks.length > 0 || context.crossLinks.length > 0) && (
        <Section title="Links to">
          {context.links.map((ref) => <Row key={ref.id} item={ref} onFocus={onFocus} />)}
          {context.crossLinks.map((ext) => <ExternalRow key={ext.card.id} item={ext} />)}
          {context.unresolvedLinks.map((title) => (
            <li key={title}>
              <span className="ref-missing" title="No card has this title yet">{title}</span>
              <button type="button" className="quiet small" onClick={() => createMissing(title)}>Create</button>
            </li>
          ))}
        </Section>
      )}
      {(context.backlinks.length > 0 || context.crossBacklinks.length > 0) && (
        <Section title="Linked from">
          {context.backlinks.map((ref) => <Row key={ref.id} item={ref} onFocus={onFocus} />)}
          {context.crossBacklinks.map((ext) => <ExternalRow key={ext.card.id} item={ext} />)}
        </Section>
      )}
      {context.mentions.length > 0 && (
        <Section title="Mentioned in">
          {context.mentions.map((ref) => (
            <Row key={ref.id} item={ref} onFocus={onFocus} action={{ label: "Link", hint: `Turn the mention in "${ref.title}" into a [[link]]`, run: () => linkMention(ref) }} />
          ))}
        </Section>
      )}
      {context.related.length > 0 && (
        <Section title="Related">
          {context.related.map((r) => (
            <Row key={r.card.id} item={r.card} onFocus={onFocus} detail={r.sharedTerms.slice(0, 3).join(", ")}
              action={{ label: "Connect", hint: "Connect as relates", run: () => connect(r.card.id) }} />
          ))}
        </Section>
      )}
    </>
  );
}

/** A card on another canvas: opens that canvas focused on it. */
function ExternalRow({ item }: { item: ExternalCardRef }) {
  return (
    <li>
      <KindGlyph kind={item.card.kind} />
      <button type="button" className="link-button" onClick={() => navigate(`/c/${item.canvasId}?focus=${item.card.id}`)} title={`On ${item.canvasTitle}`}>{item.card.title}</button>
      <span className="ref-detail">↗ {item.canvasTitle}</span>
      <span />
    </li>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="panel-section">
      <h4>{title}</h4>
      <ul className="refs">{children}</ul>
    </section>
  );
}

export function Row({ item, onFocus, detail, action }: { item: CardRef; onFocus: (id: string) => void; detail?: string; action?: { label: string; hint: string; run: () => void } }) {
  return (
    <li>
      <KindGlyph kind={item.kind} />
      <button type="button" className="link-button" onClick={() => onFocus(item.id)} title={item.regionTitle ?? "Loose card"}>{item.title}</button>
      {detail ? <span className="ref-detail">{detail}</span> : <span />}
      {action ? <button type="button" className="quiet small" title={action.hint} onClick={action.run}>{action.label}</button> : <span />}
    </li>
  );
}
