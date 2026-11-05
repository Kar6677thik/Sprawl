import { createContext, memo, useContext } from "react";
import { Handle, NodeResizer, Position, type NodeProps } from "@xyflow/react";
import type { CardNode, RegionNode, SketchNode } from "./flow";
import type { ZoomLevel } from "./zoom";
import { Body, KindGlyph } from "./parts";
import { shapeOutline } from "./shapes";
import { MermaidView } from "./MermaidView";
import { ChartView } from "./ChartView";

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
  const diagram = region.layout === "flow-down" || region.layout === "flow-right";
  return (
    <div className={`region${region.parentId ? " is-sub" : ""}${diagram ? " is-diagram" : ""}${data.group ? " is-group" : ""}${selected ? " is-selected" : ""}`}
      data-tint={region.tint ?? undefined}>
      <Anchors />
      {data.group && <Ports />}
      <header className="region-header">
        {diagram && (
          <svg className="region-flow" viewBox="0 0 12 12" width="12" height="12" aria-label={region.layout === "flow-right" ? "Diagram, flows right" : "Diagram, flows down"}>
            <path d={region.layout === "flow-right" ? "M1.5 6h8M7 3.5 9.5 6 7 8.5" : "M6 1.5v8M3.5 7 6 9.5 8.5 7"} fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
        <span className="region-title">{region.title}</span>
        {level !== "near" && <span className="region-count">{cardCount}</span>}
        {region.placement === "manual" && level !== "far" && <span className="pin" title="Placed by hand" aria-label="Placed by hand" />}
        <BackLink regionId={region.id} />
        <PlayButton regionId={region.id} />
      </header>
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

export const CardView = memo(function CardView({ data, selected }: NodeProps<CardNode>) {
  const { card, fresh, diagram } = data;
  const level = useContext(ZoomLevelContext);
  // Rendered content (a diagram, chart or table) is shown from the titles zoom on, resizable by the owner when selected.
  if ((card.kind === "mermaid" || card.kind === "chart" || card.kind === "table") && level !== "far") {
    return (
      <article className={`card kind-${card.kind} is-content${selected ? " is-selected" : ""}${fresh ? " is-fresh" : ""}`} data-tint={card.tint ?? undefined} aria-label={card.title}>
        <DetailBadge regionId={card.detail} />
        <NodeResizer isVisible={selected} minWidth={240} minHeight={160} lineClassName="resize-line" handleClassName="resize-handle" />
        <Anchors />
        {diagram && <Ports />}
        <header className="card-header">
          <KindGlyph kind={card.kind} />
          <h3 className="card-title">{card.title}</h3>
        </header>
        <div className="card-mermaid">
          {card.kind === "mermaid" ? <MermaidView source={card.body} /> : card.kind === "chart" ? <ChartView spec={card.body} /> : <div className="card-table"><Body markdown={card.body} /></div>}
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
        <svg className="shape-svg" width={card.w} height={card.h} viewBox={`0 0 ${card.w} ${card.h}`} aria-hidden="true">
          {shapeOutline(card.shape, card.w, card.h, 1.5)}
        </svg>
        <span className="shape-label">{card.title}</span>
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
      {card.createdBy === "ai" && <span className="ai-mark" title="Added by Claude" aria-label="Added by Claude" />}
      {level !== "far" && (
        <>
          <header className="card-header">
            <KindGlyph kind={card.kind} />
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
