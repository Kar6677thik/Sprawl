import { createContext, memo, useContext } from "react";
import { Handle, NodeResizer, Position, type NodeProps } from "@xyflow/react";
import type { CardNode, RegionNode, SketchNode } from "./flow";
import type { ZoomLevel } from "./zoom";
import { Body, KindGlyph } from "./parts";
import { shapeOutline } from "./shapes";
import { MermaidView } from "./MermaidView";
import { ChartView } from "./ChartView";
import { currentTheme } from "./MermaidView";
import type { LoopBadge } from "./loops";
import type { StepMark } from "../api/types";
import { Icon } from "./Icon";

export const ZoomLevelContext = createContext<ZoomLevel>("mid");

/** Drill-down: open a card's detail region, and go back from a detail region to the card it explains. */
export const DetailContext = createContext<{ open: (regionId: string) => void; owners: ReadonlyMap<string, { id: string; title: string }>; back: (cardId: string) => void }>(
  { open: () => {}, owners: new Map(), back: () => {} });

function DetailBadge({ regionId }: { regionId: string | null | undefined }) {
  const details = useContext(DetailContext);
  if (!regionId) return null;
  return (
    <button type="button" className="detail-badge" title="Zoom into the detail" aria-label="Zoom into the detail" onPointerDown={(e) => e.stopPropagation()}
      onClick={(e) => { e.stopPropagation(); details.open(regionId); }}>
      <svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><path d="M7 1.5h3.5V5M10.5 1.5 6.5 5.5M5 10.5H1.5V7M1.5 10.5l4-4" fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" /></svg>
    </button>
  );
}

function BackLink({ regionId }: { regionId: string }) {
  const details = useContext(DetailContext);
  const owner = details.owners.get(regionId);
  if (!owner) return null;
  return (
    <button type="button" className="detail-back" title={`Back to ${owner.title}`} onPointerDown={(e) => e.stopPropagation()}
      onClick={(e) => { e.stopPropagation(); details.back(owner.id); }}>↑ {owner.title}</button>
  );
}

/** Loop badges (R1, B1) per causal-loop region, in region coordinates. */
export const LoopContext = createContext<ReadonlyMap<string, LoopBadge[]>>(new Map());

/** The current walkthrough step's scenario marks, by item id. */
export const MarkContext = createContext<ReadonlyMap<string, StepMark>>(new Map());

const MARK_LABEL: Record<StepMark["state"], string> = { down: "Down", slow: "Slow", hot: "Hot", added: "New", removed: "Removed", changed: "Changed" };

/** A scenario mark's tag above a marked item: its state, and its note when it has one. */
function MarkTag({ id }: { id: string }) {
  const mark = useContext(MarkContext).get(id);
  if (!mark) return null;
  return <span className={`mark-tag mark-tag-${mark.state}`}>{MARK_LABEL[mark.state]}{mark.note ? ` · ${mark.note}` : ""}</span>;
}

function LoopBadges({ regionId }: { regionId: string }) {
  const badges = useContext(LoopContext).get(regionId) ?? [];
  const kind = (b: LoopBadge) => b.polarity === "R" ? "reinforcing" : b.polarity === "B" ? "balancing" : "polarity unknown (a link has no + or −)";
  return (
    <>
      {badges.map((b) => (
        <svg key={b.name} className={`loop-badge loop-${(b.polarity ?? "u").toLowerCase()}`} style={{ left: b.x - 22, top: b.y - 22 }} width="44" height="44" viewBox="-22 -22 44 44"
          role="img" aria-label={`Loop ${b.name}, ${kind(b)}${b.delayed ? ", with a delay" : ""}`}>
          <title>{`${b.name}: ${kind(b)}${b.delayed ? ", with a delay" : ""}`}</title>
          <path d={b.clockwise ? "M19 0 A19 19 0 1 1 0 -19" : "M-19 0 A19 19 0 1 0 0 -19"} fill="none" strokeWidth="1.6" />
          <path d={b.clockwise ? "M-5 -23 L1 -19 L-5 -15" : "M5 -23 L-1 -19 L5 -15"} fill="none" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
          <circle r="14" />
          <text y="4.5" textAnchor="middle">{b.name}</text>
        </svg>
      ))}
    </>
  );
}

/** Regions that have walkthroughs, and how to play one (a ▶ in the region's header). */
export const WalkthroughContext = createContext<{ regions: ReadonlySet<string>; play: (regionId: string) => void }>({ regions: new Set(), play: () => {} });

/**
 * Diagram nodes get a port on each side, shown on hover: drag from one to another node to connect them, as in draw.io.
 * (Edges themselves float to the facing sides, so the port only starts the drag.)
 */
function Ports() {
  return (
    <>
      <Handle id="t" type="source" position={Position.Top} className="port" />
      <Handle id="r" type="source" position={Position.Right} className="port" />
      <Handle id="b" type="source" position={Position.Bottom} className="port" />
      <Handle id="l" type="source" position={Position.Left} className="port" />
    </>
  );
}

/** Connections attach to invisible handles; the canvas never shows connection dots. */
function Anchors() {
  return (
    <>
      <Handle type="target" position={Position.Top} className="anchor" isConnectable={false} />
      <Handle type="source" position={Position.Bottom} className="anchor" isConnectable={false} />
    </>
  );
}

export const RegionView = memo(function RegionView({ data, selected }: NodeProps<RegionNode>) {
  const { region, cardCount, minis } = data;
  const level = useContext(ZoomLevelContext);
  if (level === "far" && !region.parentId)
    return (
      <div className={`region is-map${selected ? " is-selected" : ""}`} data-tint={region.tint ?? undefined}>
        <Anchors />
        {minis.map((box) => (
          <span key={box.id} className={`mini mini-${box.kind}`} style={{ left: box.x, top: box.y, width: box.w, height: box.h }} />
        ))}
        <div className="region-label">
          <span className="region-title">{region.title}</span>
          <span className="region-count">{minis.filter((box) => box.kind === "card").length} cards</span>
        </div>
      </div>
    );
  const diagram = region.layout === "flow-down" || region.layout === "flow-right" || region.layout === "loops";
  return (
    <div className={`region${region.parentId ? " is-sub" : ""}${diagram ? " is-diagram" : ""}${data.group ? " is-group" : ""}${selected ? " is-selected" : ""}`}
      data-tint={region.tint ?? undefined}>
      <Anchors />
      {data.group && <Ports />}
      <header className="region-header">
        {diagram && (
          <svg className="region-flow" viewBox="0 0 12 12" width="12" height="12"
            aria-label={region.layout === "loops" ? "Causal-loop diagram" : region.layout === "flow-right" ? "Diagram, flows right" : "Diagram, flows down"}>
            <path d={region.layout === "loops" ? "M10 6a4 4 0 1 1-1.2-2.85M9.2 1.2v2.2H7" : region.layout === "flow-right" ? "M1.5 6h8M7 3.5 9.5 6 7 8.5" : "M6 1.5v8M3.5 7 6 9.5 8.5 7"}
              fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
        <span className="region-title">{region.title}</span>
        {level !== "near" && <span className="region-count">{cardCount}</span>}
        {region.placement === "manual" && level !== "far" && <span className="pin" title="Placed by hand" aria-label="Placed by hand" />}
        <BackLink regionId={region.id} />
        <PlayButton regionId={region.id} />
      </header>
      {region.layout === "loops" && <LoopBadges regionId={region.id} />}
      <MarkTag id={region.id} />
    </div>
  );
});

function PlayButton({ regionId }: { regionId: string }) {
  const walkthroughs = useContext(WalkthroughContext);
  if (!walkthroughs.regions.has(regionId)) return null;
  return (
    <button type="button" className="region-play" title="Play the walkthrough (W)" onPointerDown={(e) => e.stopPropagation()}
      onClick={(e) => { e.stopPropagation(); walkthroughs.play(regionId); }}>
      <svg viewBox="0 0 12 12" width="10" height="10" aria-hidden="true"><path d="M3 1.5v9l7.5-4.5z" fill="currentColor" /></svg>
      Walkthrough
    </button>
  );
}

/**
 * A widget runs as its own document in a sandboxed frame: scripts may run, but without allow-same-origin the frame is an
 * opaque origin with no access to Sprawl's cookies, storage or API, and the server's policy for it allows no network at all.
 * nodrag/nowheel let the owner use its controls without moving the canvas.
 */
function WidgetFrame({ canvasId, card }: { canvasId: string; card: CardNode["data"]["card"] }) {
  const src = `/api/v1/canvases/${encodeURIComponent(canvasId)}/cards/${encodeURIComponent(card.id)}/widget?theme=${currentTheme()}&v=${encodeURIComponent(card.updatedAt)}`;
  return <iframe className="widget-frame nodrag nowheel nopan" title={card.title} src={src} sandbox="allow-scripts" referrerPolicy="no-referrer" loading="lazy" />;
}

export const CardView = memo(function CardView({ data, selected }: NodeProps<CardNode>) {
  const { card, fresh, diagram, canvasId } = data;
  const level = useContext(ZoomLevelContext);
  // Rendered content (a diagram, chart or table) is shown from the titles zoom on, resizable by the owner when selected.
  if ((card.kind === "mermaid" || card.kind === "chart" || card.kind === "table" || card.kind === "widget") && level !== "far") {
    return (
      <article className={`card kind-${card.kind} is-content${selected ? " is-selected" : ""}${fresh ? " is-fresh" : ""}`} data-tint={card.tint ?? undefined} aria-label={card.title}>
        <DetailBadge regionId={card.detail} />
        <MarkTag id={card.id} />
        <NodeResizer isVisible={selected} minWidth={240} minHeight={160} lineClassName="resize-line" handleClassName="resize-handle" />
        <Anchors />
        {diagram && <Ports />}
        <header className="card-header">
          <KindGlyph kind={card.kind} />
          <h3 className="card-title">{card.title}</h3>
        </header>
        <div className="card-mermaid">
          {card.kind === "mermaid" ? <MermaidView source={card.body} />
            : card.kind === "chart" ? <ChartView spec={card.body} />
            : card.kind === "widget" ? <WidgetFrame canvasId={canvasId} card={card} />
            : <div className="card-table"><Body markdown={card.body} /></div>}
        </div>
      </article>
    );
  }
  if (card.shape && level !== "far") {
    const classes = ["shape-node", `shape-${card.shape}`];
    if (selected) classes.push("is-selected");
    if (fresh) classes.push("is-fresh");
    if (card.emphasis === "key") classes.push("is-key");
    return (
      <div className={classes.join(" ")} data-tint={card.tint ?? undefined} aria-label={card.title} title={card.body || undefined}>
        <Anchors />
        <Ports />
        <DetailBadge regionId={card.detail} />
        <MarkTag id={card.id} />
        <svg className="shape-svg" width={card.w} height={card.h} viewBox={`0 0 ${card.w} ${card.h}`} aria-hidden="true">
          {shapeOutline(card.shape, card.w, card.h, 1.5)}
        </svg>
        <div className="shape-content">
          {card.shape !== "person" && card.shape !== "text" && <Icon name={card.icon} className="shape-glyph" />}
          <span className="shape-label">{card.title}</span>
        </div>
      </div>
    );
  }
  const classes = ["card", `kind-${card.kind}`];
  if (card.emphasis === "key") classes.push("is-key");
  if (selected) classes.push("is-selected");
  if (fresh) classes.push("is-fresh");
  if (level === "far") classes.push("is-bar");
  return (
    <article className={classes.join(" ")} data-tint={card.tint ?? undefined} aria-label={card.title}>
      <Anchors />
      {diagram && <Ports />}
      <DetailBadge regionId={card.detail} />
      <MarkTag id={card.id} />
      {card.createdBy === "ai" && <span className="ai-mark" title="Added by Claude" aria-label="Added by Claude" />}
      {level !== "far" && (
        <>
          <header className="card-header">
            {card.icon ? <Icon name={card.icon} size={14} className="card-icon" /> : <KindGlyph kind={card.kind} />}
            <h3 className="card-title">{card.title}</h3>
          </header>
          {level === "near" && card.kind === "link" && card.url && (
            <a className="card-url" href={card.url} target="_blank" rel="noopener noreferrer nofollow" onPointerDown={(e) => e.stopPropagation()}>
              {safeHost(card.url)}
            </a>
          )}
          {level === "near" && (
            <div className="card-body">
              {card.kind === "code" ? (
                <pre className="card-code" data-language={card.language ?? undefined}><code>{card.body}</code></pre>
              ) : (
                card.body && <Body markdown={card.body} />
              )}
            </div>
          )}
        </>
      )}
    </article>
  );
});

function safeHost(url: string) {
  try { return new URL(url).host; } catch { return url; }
}

/** A freehand stroke. Drawn in its own box; the box is what selection and dragging act on. */
export const SketchView = memo(function SketchView({ data, selected }: NodeProps<SketchNode>) {
  const { sketch } = data;
  return (
    <svg className={`sketch${selected ? " is-selected" : ""}`} data-tint={sketch.tint ?? undefined} width={sketch.w} height={sketch.h}
      viewBox={`0 0 ${sketch.w} ${sketch.h}`} aria-label="Sketch" role="img">
      <path d={sketch.path} fill="none" stroke="currentColor" strokeWidth={sketch.stroke} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
});

export const nodeTypes = { region: RegionView, card: CardView, sketch: SketchView };
