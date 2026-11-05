import { useEffect, useState, type ReactNode } from "react";
import { api } from "../api/client";
import type { CanvasInsights, Intent } from "../api/types";
import { Row } from "./CardLinks";

interface Props {
  canvasId: string;
  version: number;
  write: (intents: Intent[], touched: string[]) => Promise<unknown>;
  onFocus: (id: string) => void;
  onTag: (tag: string) => void;
  say: (text: string) => void;
  onClose: () => void;
}

/**
 * What the canvas is missing: gaps between regions that share vocabulary but no connection, orphans, `[[links]]` with
 * no card, open questions, contradictions, hubs and tags. The same analysis Claude gets from analyze_canvas.
 */
export function InsightsPanel({ canvasId, version, write, onFocus, onTag, say, onClose }: Props) {
  const [insights, setInsights] = useState<CanvasInsights | null>(null);
  useEffect(() => {
    let live = true;
    api.insights(canvasId).then((i) => { if (live) setInsights(i); }, () => { if (live) setInsights(null); });
    return () => { live = false; };
  }, [canvasId, version]);

  const bridge = (firstId: string, firstTitle: string, secondId: string, secondTitle: string) => void write([
    { type: "addCard", ref: "q", kind: "question", title: `How does ${firstTitle} relate to ${secondTitle}?`, region: firstId },
    { type: "connect", from: "q", to: secondId, kind: "relates" },
  ], []);
  const copyPrompt = () => void navigator.clipboard.writeText(`Use the Sprawl find_gaps prompt on canvas ${canvasId}: bridge the gaps analyze_canvas reports, write cards for dangling links, and connect the orphans.`)
    .then(() => say("Prompt copied: paste it into Claude."), () => say("Could not copy to the clipboard."));

  return (
    <aside className="panel" aria-label="Insights">
      <header className="panel-header">
        <span className="panel-title">Insights</span>
        <span className="panel-actions">
          <button type="button" className="quiet small" onClick={copyPrompt} title="Copy a prompt that asks Claude to work through these">Ask Claude</button>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
        </span>
      </header>
      {!insights ? <p className="panel-empty">Reading the canvas…</p> : (
        <div className="panel-body">
          <p className="insight-stats">
            {insights.cards} cards · {insights.regions} regions · {insights.connections} connections · {insights.textLinks} text links
          </p>
          <Group title="Gaps between regions" hint="They share these terms but nothing connects them." count={insights.gaps.length}>
            {insights.gaps.map((gap) => (
              <li key={`${gap.firstId}:${gap.secondId}`} className="gap">
                <span className="gap-pair">
                  <button type="button" className="link-button" onClick={() => onFocus(gap.firstId)}>{gap.firstTitle}</button>
                  <span aria-hidden="true">↔</span>
                  <button type="button" className="link-button" onClick={() => onFocus(gap.secondId)}>{gap.secondTitle}</button>
                </span>
                <span className="ref-detail">{gap.sharedTerms.join(", ")}</span>
                <button type="button" className="quiet small" title="Add a bridging question to the first region, connected to the second"
                  onClick={() => bridge(gap.firstId, gap.firstTitle, gap.secondId, gap.secondTitle)}>Bridge</button>
              </li>
            ))}
          </Group>
          <Group title="Links with no card" hint="Write these cards and the links resolve." count={insights.danglingLinks.length}>
            {insights.danglingLinks.map((d) => (
              <li key={d.target}>
                <span className="ref-missing">{d.target}</span>
                <span className="ref-detail">from {d.from.map((f) => f.title).join(", ")}</span>
                <button type="button" className="quiet small" onClick={() => void write([{ type: "addCard", kind: "note", title: d.target, ...(d.from[0]?.regionId ? { region: d.from[0].regionId } : {}) }], [])}>Create</button>
              </li>
            ))}
          </Group>
          <Group title="Orphans" hint="No connection, link or backlink." count={insights.orphans.length}>
            {insights.orphans.map((ref) => <Row key={ref.id} item={ref} onFocus={onFocus} detail={ref.regionTitle ?? "loose"} />)}
          </Group>
          <Group title="Open questions" count={insights.openQuestions.length}>
            {insights.openQuestions.map((ref) => <Row key={ref.id} item={ref} onFocus={onFocus} detail={ref.regionTitle ?? "loose"} />)}
          </Group>
          <Group title="Contradictions" count={insights.contradictions.length}>
            {insights.contradictions.map((c) => (
              <li key={c.connectionId} className="gap">
                <span className="gap-pair">
                  <button type="button" className="link-button" onClick={() => onFocus(c.fromId)}>{c.fromTitle}</button>
                  <span aria-hidden="true">⟂</span>
                  <button type="button" className="link-button" onClick={() => onFocus(c.toId)}>{c.toTitle}</button>
                </span>
                <span className="ref-detail">{c.label ?? ""}</span>
                <span />
              </li>
            ))}
          </Group>
          <Group title="Hubs" hint="The most connected cards." count={insights.hubs.length}>
            {insights.hubs.map((h) => <Row key={h.card.id} item={h.card} onFocus={onFocus} detail={`${h.degree} links`} />)}
          </Group>
          {insights.tags.length > 0 && (
            <section className="panel-section">
              <h4>Tags</h4>
              <div className="tag-row">
                {insights.tags.map((t) => <button key={t.tag} type="button" className="tag-chip" onClick={() => onTag(t.tag)}>#{t.tag} <span>{t.count}</span></button>)}
              </div>
            </section>
          )}
        </div>
      )}
    </aside>
  );
}

function Group({ title, hint, count, children }: { title: string; hint?: string; count: number; children: ReactNode }) {
  if (count === 0) return null;
  return (
    <section className="panel-section">
      <h4 title={hint}>{title} <span className="count">{count}</span></h4>
      <ul className="refs">{children}</ul>
    </section>
  );
}
