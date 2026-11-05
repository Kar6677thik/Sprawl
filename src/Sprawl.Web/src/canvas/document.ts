import { kindOf, type Card, type CanvasDocument, type CanvasEvent, type CanvasMeta, type Connection, type Region, type Sketch } from "../api/types";

export type ApplyResult =
  | { status: "applied"; doc: CanvasDocument; added: string[] }
  | { status: "ignored" }
  | { status: "gap"; expected: number; received: number };

/**
 * Apply one SSE batch to a document. Ops carry full after-images (null = deleted), so applying is "set or remove".
 * Events at or below the current version are ignored (our own writes, or replays); a jump means events were missed
 * and the caller must refetch.
 */
export function applyEvent(doc: CanvasDocument, event: CanvasEvent): ApplyResult {
  if (event.canvasId !== doc.meta.id || event.version <= doc.version) return { status: "ignored" };
  if (event.version !== doc.version + 1) return { status: "gap", expected: doc.version + 1, received: event.version };
  const regions = new Map(doc.regions.map((r) => [r.id, r]));
  const cards = new Map(doc.cards.map((c) => [c.id, c]));
  const connections = new Map(doc.connections.map((c) => [c.id, c]));
  const sketches = new Map((doc.sketches ?? []).map((s) => [s.id, s]));
  let meta = doc.meta;
  const added: string[] = [];
  for (const op of event.ops) {
    const after = op.after;
    switch (kindOf(op.entityId)) {
      case "canvas":
        if (after) meta = after as CanvasMeta;
        break;
      case "region":
        set(regions, op.entityId, after as Region | null);
        break;
      case "card":
        if (op.type === "card.create") added.push(op.entityId);
        set(cards, op.entityId, after as Card | null);
        break;
      case "connection":
        set(connections, op.entityId, after as Connection | null);
        break;
      case "sketch":
        set(sketches, op.entityId, after as Sketch | null);
        break;
    }
  }
  return {
    status: "applied",
    added,
    doc: {
      ...doc,
      meta,
      version: event.version,
      updatedAt: event.createdAt,
      regions: orderRegions([...regions.values()]),
      cards: [...cards.values()],
      connections: [...connections.values()],
      sketches: [...sketches.values()],
    },
  };
}

function set<T>(map: Map<string, T>, id: string, value: T | null) {
  if (value === null || value === undefined) map.delete(id);
  else map.set(id, value);
}

/** Parents before children: React Flow requires a parent node to precede its children. */
export function orderRegions(regions: Region[]): Region[] {
  return [...regions].sort((a, b) => Number(a.parentId !== null) - Number(b.parentId !== null) || a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id));
}

/**
 * After a 412, may a local change based on `base` be replayed against `fresh`? Only when none of the entities it
 * touches changed (or disappeared) in between; otherwise the owner's edit would silently overwrite someone else's.
 */
export function canRebase(base: CanvasDocument, fresh: CanvasDocument, entityIds: string[]): boolean {
  return entityIds.every((id) => {
    const before = find(base, id);
    const now = find(fresh, id);
    return before !== undefined && now !== undefined && JSON.stringify(before) === JSON.stringify(now);
  });
}

export function find(doc: CanvasDocument, id: string): Region | Card | Connection | Sketch | undefined {
  switch (kindOf(id)) {
    case "region": return doc.regions.find((r) => r.id === id);
    case "card": return doc.cards.find((c) => c.id === id);
    case "connection": return doc.connections.find((c) => c.id === id);
    case "sketch": return doc.sketches?.find((s) => s.id === id);
    default: return undefined;
  }
}

/**
 * Optimistically apply a drag locally while the server confirms (the SSE event then replaces it with the truth).
 * A `container` moves a card into a region ("rg_…") or onto the open canvas ("canvas").
 */
export function moveLocally(doc: CanvasDocument, moves: { id: string; x: number; y: number; container?: string; asAuto?: boolean }[]): CanvasDocument {
  const byId = new Map(moves.map((m) => [m.id, m]));
  const placement = (m: { asAuto?: boolean }) => (m.asAuto ? "auto" : "manual") as "auto" | "manual";
  return {
    ...doc,
    regions: doc.regions.map((r) => {
      const m = byId.get(r.id);
      return m ? { ...r, x: m.x, y: m.y, placement: placement(m) } : r;
    }),
    cards: doc.cards.map((c) => {
      const m = byId.get(c.id);
      if (!m) return c;
      const regionId = m.container === undefined ? c.regionId : m.container === "canvas" ? null : m.container;
      return { ...c, x: m.x, y: m.y, regionId, placement: regionId === null ? "manual" : placement(m) };
    }),
    sketches: (doc.sketches ?? []).map((s) => {
      const m = byId.get(s.id);
      return m ? { ...s, x: m.x, y: m.y } : s;
    }),
  };
}
