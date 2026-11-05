import type { CanvasDocument, Walkthrough } from "../api/types";

export interface Lit {
  /** Cards, regions and sketches shown at full strength. */
  nodes: Set<string>;
  /** Connections lit and animated. */
  edges: Set<string>;
}

/**
 * What one step lights up. A highlighted region lights its contents (cards, sub-regions and their cards); a connection
 * is lit when named, or when both its ends are lit, so "the browser and the API" also lights the call between them.
 */
export function litFor(doc: Pick<CanvasDocument, "cards" | "regions" | "connections">, ids: readonly string[]): Lit {
  const nodes = new Set<string>();
  const edges = new Set<string>();
  const regionIds = new Set(doc.regions.map((r) => r.id));
  for (const id of ids) {
    if (id.startsWith("cx_")) { edges.add(id); continue; }
    nodes.add(id);
    if (!regionIds.has(id)) continue;
    const inside = new Set([id, ...doc.regions.filter((r) => r.parentId === id).map((r) => r.id)]);
    for (const r of inside) nodes.add(r);
    for (const card of doc.cards) if (card.regionId && inside.has(card.regionId)) nodes.add(card.id);
  }
  for (const c of doc.connections) {
    if (edges.has(c.id)) { nodes.add(c.sourceId); nodes.add(c.targetId); continue; }
    if (nodes.has(c.sourceId) && nodes.has(c.targetId)) edges.add(c.id);
  }
  return { nodes, edges };
}

/** Walkthroughs in reading order: by region, then creation. */
export function walkthroughsOf(doc: Pick<CanvasDocument, "walkthroughs">): Walkthrough[] {
  return [...(doc.walkthroughs ?? [])].sort((a, b) => a.regionId.localeCompare(b.regionId) || a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id));
}
