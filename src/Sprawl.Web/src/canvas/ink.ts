export type Point = { x: number; y: number };

/** A finished stroke: its box (canvas coordinates) and SVG path data relative to that box. */
export interface Stroke {
  x: number;
  y: number;
  w: number;
  h: number;
  path: string;
}

/** Drop points closer than `min` canvas pixels to the last kept one, so a slow hand does not make a huge path. */
export function thin(points: Point[], min = 2): Point[] {
  const kept: Point[] = [];
  for (const p of points) {
    const last = kept[kept.length - 1];
    if (!last || Math.hypot(p.x - last.x, p.y - last.y) >= min) kept.push(p);
  }
  const end = points[points.length - 1];
  const lastKept = kept[kept.length - 1];
  if (end && lastKept && lastKept !== end && Math.hypot(end.x - lastKept.x, end.y - lastKept.y) >= 0.5) kept.push(end);
  return kept;
}

const r = (n: number) => Math.round(n * 10) / 10;

/**
 * Points (canvas coordinates) to a stroke: a box padded by the line width, and a smooth path through the midpoints
 * (quadratic curves with the raw points as controls), using only M, L and Q so the server's path check accepts it.
 * Returns null for a click with no movement.
 */
export function toStroke(raw: Point[], width: number): Stroke | null {
  const points = thin(raw);
  if (points.length < 2) return null;
  const pad = Math.ceil(width / 2) + 1;
  const minX = Math.min(...points.map((p) => p.x)) - pad;
  const minY = Math.min(...points.map((p) => p.y)) - pad;
  const maxX = Math.max(...points.map((p) => p.x)) + pad;
  const maxY = Math.max(...points.map((p) => p.y)) + pad;
  const local = points.map((p) => ({ x: r(p.x - minX), y: r(p.y - minY) }));
  let path = `M${local[0]!.x} ${local[0]!.y}`;
  if (local.length === 2) path += ` L${local[1]!.x} ${local[1]!.y}`;
  else {
    for (let i = 1; i < local.length - 1; i++) {
      const p = local[i]!;
      const next = local[i + 1]!;
      path += ` Q${p.x} ${p.y} ${r((p.x + next.x) / 2)} ${r((p.y + next.y) / 2)}`;
    }
    const last = local[local.length - 1]!;
    path += ` L${last.x} ${last.y}`;
  }
  return { x: Math.round(minX), y: Math.round(minY), w: Math.max(1, Math.ceil(maxX - minX)), h: Math.max(1, Math.ceil(maxY - minY)), path };
}
