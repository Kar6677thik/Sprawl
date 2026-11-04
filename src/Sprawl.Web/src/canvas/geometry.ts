import type { CanvasDocument } from "../api/types";

export interface Rect { x: number; y: number; w: number; h: number }
export type Side = "top" | "right" | "bottom" | "left";

const center = (r: Rect) => ({ x: r.x + r.w / 2, y: r.y + r.h / 2 });

/**
 * Where an edge between two rectangles should attach: on each one's border, on the side facing the other, at the
 * point where the line between their centres leaves the rectangle. Edges then never start on the far side of a card.
 */
export function floatingEndpoints(source: Rect, target: Rect) {
  const s = border(source, center(target));
  const t = border(target, center(source));
  return { sx: s.x, sy: s.y, sourceSide: s.side, tx: t.x, ty: t.y, targetSide: t.side };
}

function border(rect: Rect, toward: { x: number; y: number }): { x: number; y: number; side: Side } {
  const c = center(rect);
  const dx = toward.x - c.x;
  const dy = toward.y - c.y;
  if (dx === 0 && dy === 0) return { x: c.x, y: rect.y, side: "top" };
  const halfW = rect.w / 2;
  const halfH = rect.h / 2;
  // Scale the direction vector until it touches the nearer pair of edges.
  const scale = Math.min(dx === 0 ? Infinity : halfW / Math.abs(dx), dy === 0 ? Infinity : halfH / Math.abs(dy));
  const x = c.x + dx * scale;
  const y = c.y + dy * scale;
  const side: Side = Math.abs(dx) * halfH > Math.abs(dy) * halfW ? (dx > 0 ? "right" : "left") : dy > 0 ? "bottom" : "top";
  return { x, y, side };
}

/** Absolute rectangles of every region, keyed by id (sub-regions include their parent's offset). */
export function regionRects(doc: CanvasDocument): Map<string, Rect & { parentId: string | null }> {
  const byId = new Map(doc.regions.map((r) => [r.id, r]));
  const result = new Map<string, Rect & { parentId: string | null }>();
  for (const region of doc.regions) {
    const parent = region.parentId ? byId.get(region.parentId) : undefined;
    result.set(region.id, { x: region.x + (parent?.x ?? 0), y: region.y + (parent?.y ?? 0), w: region.w, h: region.h, parentId: region.parentId });
  }
  return result;
}

/**
 * The region a point on the canvas falls in: the innermost one (sub-regions win over their parent), or null for the
 * open canvas. Used for drag-to-region and for quick-adding a card under the cursor.
 */
export function regionAt(doc: CanvasDocument, point: { x: number; y: number }, exclude?: string): string | null {
  let best: { id: string; depth: number; area: number } | null = null;
  for (const [id, rect] of regionRects(doc)) {
    if (id === exclude) continue;
    if (point.x < rect.x || point.x > rect.x + rect.w || point.y < rect.y || point.y > rect.y + rect.h) continue;
    const depth = rect.parentId ? 1 : 0;
    const area = rect.w * rect.h;
    if (!best || depth > best.depth || (depth === best.depth && area < best.area)) best = { id, depth, area };
  }
  return best?.id ?? null;
}

/** A point in canvas coordinates expressed relative to a region (or unchanged for the open canvas). */
export function relativeTo(doc: CanvasDocument, regionId: string | null, point: { x: number; y: number }) {
  if (!regionId) return { x: Math.round(point.x), y: Math.round(point.y) };
  const rect = regionRects(doc).get(regionId);
  return rect ? { x: Math.round(point.x - rect.x), y: Math.round(point.y - rect.y) } : { x: Math.round(point.x), y: Math.round(point.y) };
}
