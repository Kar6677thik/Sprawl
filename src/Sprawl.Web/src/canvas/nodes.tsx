import { createContext, memo, useContext } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import type { CardNode, RegionNode, SketchNode } from "./flow";
import type { ZoomLevel } from "./zoom";
import { Body, KindGlyph } from "./parts";

export const ZoomLevelContext = createContext<ZoomLevel>("mid");

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
  return (
    <div className={`region${region.parentId ? " is-sub" : ""}${selected ? " is-selected" : ""}`} data-tint={region.tint ?? undefined}>
      <Anchors />
      <header className="region-header">
        <span className="region-title">{region.title}</span>
        {level !== "near" && <span className="region-count">{cardCount}</span>}
        {region.placement === "manual" && level !== "far" && <span className="pin" title="Placed by hand" aria-label="Placed by hand" />}
      </header>
    </div>
  );
});

export const CardView = memo(function CardView({ data, selected }: NodeProps<CardNode>) {
  const { card, fresh } = data;
  const level = useContext(ZoomLevelContext);
  const classes = ["card", `kind-${card.kind}`];
  if (card.emphasis === "key") classes.push("is-key");
  if (selected) classes.push("is-selected");
  if (fresh) classes.push("is-fresh");
  if (level === "far") classes.push("is-bar");
  return (
    <article className={classes.join(" ")} data-tint={card.tint ?? undefined} aria-label={card.title}>
      <Anchors />
      {card.createdBy === "ai" && <span className="ai-mark" title="Added by Claude" aria-label="Added by Claude" />}
      {level !== "far" && (
        <>
          <header className="card-header">
            <KindGlyph kind={card.kind} />
            <h3 className="card-title">{card.title}</h3>
          </header>
          {level === "near" && (
            <div className="card-body">
              {card.kind === "link" && card.url && (
                <a className="card-url" href={card.url} target="_blank" rel="noopener noreferrer nofollow" onPointerDown={(e) => e.stopPropagation()}>
                  {safeHost(card.url)}
                </a>
              )}
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
