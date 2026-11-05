import { BaseEdge, getBezierPath, getSmoothStepPath, Position, useInternalNode, type EdgeProps } from "@xyflow/react";
import { curvedPath, floatingEndpoints, type Rect, type Side } from "./geometry";

const POSITION: Record<Side, Position> = { top: Position.Top, right: Position.Right, bottom: Position.Bottom, left: Position.Left };

/** An edge that attaches to the facing sides of its two nodes instead of fixed handles. */
export function FloatingEdge({ id, source, target, markerEnd, markerStart, style, label, interactionWidth, data }: EdgeProps) {
  const from = useInternalNode(source);
  const to = useInternalNode(target);
  if (!from || !to || from.hidden || to.hidden) return null;
  const rect = (node: NonNullable<typeof from>): Rect => ({
    x: node.internals.positionAbsolute.x,
    y: node.internals.positionAbsolute.y,
    w: node.measured.width ?? node.width ?? 0,
    h: node.measured.height ?? node.height ?? 0,
  });
  const { sx, sy, sourceSide, tx, ty, targetSide } = floatingEndpoints(rect(from), rect(to));
  const ends = {
    sourceX: sx, sourceY: sy, sourcePosition: POSITION[sourceSide],
    targetX: tx, targetY: ty, targetPosition: POSITION[targetSide],
  };
  // Diagram edges run at right angles with rounded corners, as in draw.io; everything else stays a soft curve.
  // Causal links curve around their loop; other diagram edges run at right angles with rounded corners, as in draw.io;
  // everything else stays a soft curve.
  const curve = data?.["curved"] ? curvedPath(rect(from), rect(to)) : null;
  const [path, labelX, labelY] = curve ? [curve.path, curve.labelX, curve.labelY]
    : data?.["orthogonal"] ? getSmoothStepPath({ ...ends, borderRadius: 10, offset: 18 }) : getBezierPath(ends);
  return (
    <BaseEdge
      id={id}
      path={path}
      {...(markerEnd ? { markerEnd } : {})}
      {...(markerStart ? { markerStart } : {})}
      {...(style ? { style } : {})}
      {...(label ? { label, labelX, labelY, labelBgPadding: [4, 2] as [number, number], labelBgBorderRadius: 4 } : {})}
      interactionWidth={interactionWidth ?? 14}
    />
  );
}

export const edgeTypes = { floating: FloatingEdge };
