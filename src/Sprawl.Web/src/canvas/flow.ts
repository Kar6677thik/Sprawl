import { MarkerType, type Edge, type Node } from "@xyflow/react";
import type { Card, CanvasDocument, Connection, Region } from "../api/types";
import { orderRegions } from "./document";
import type { ZoomLevel } from "./zoom";

/** A descendant's rectangle relative to its top-level region, drawn as the zoomed-out miniature. */
export type MiniBox = { id: string; x: number; y: number; w: number; h: number; kind: "card" | "region" };
export type RegionNodeData = { region: Region; cardCount: number; minis: MiniBox[] };
export type CardNodeData = { card: Card; fresh: boolean };
export type RegionNode = Node<RegionNodeData, "region">;
export type CardNode = Node<CardNodeData, "card">;
export type SprawlNode = RegionNode | CardNode;

const DIRECTIONAL = new Set(["leads-to", "depends-on", "example-of"]);

/**
 * Map a canvas document onto React Flow. Positions come straight from the server: cards are relative to their region
 * and sub-regions to their parent, which is exactly React Flow's `parentId` model. Parents are emitted before children.
 */
export function toNodes(doc: CanvasDocument, fresh: ReadonlySet<string> = new Set(), level: ZoomLevel = "mid"): SprawlNode[] {
  // Zoomed out, only top-level regions are nodes: each draws a miniature of its contents under its title, so labels
  // never collide with child nodes (which always render above their parent) and edges to hidden cards drop away.
  const far = level === "far";
  const minis = miniatures(doc);
  const counts = new Map<string, number>();
  for (const card of doc.cards) if (card.regionId) counts.set(card.regionId, (counts.get(card.regionId) ?? 0) + 1);
  const regions: RegionNode[] = orderRegions(doc.regions).map((region) => ({
    id: region.id,
    type: "region",
    position: { x: region.x, y: region.y },
    width: region.w,
    height: region.h,
    ...(region.parentId ? { parentId: region.parentId } : {}),
    data: { region, cardCount: counts.get(region.id) ?? 0, minis: minis.get(region.id) ?? [] },
    className: region.placement === "manual" ? "is-manual" : undefined,
    zIndex: region.parentId ? 1 : 0,
    hidden: far && region.parentId !== null,
  }));
  const cards: CardNode[] = doc.cards.map((card) => ({
    id: card.id,
    type: "card",
    position: { x: card.x, y: card.y },
    width: card.w,
    height: card.h,
    ...(card.regionId ? { parentId: card.regionId } : {}),
    data: { card, fresh: fresh.has(card.id) },
    zIndex: 3,
    hidden: far && card.regionId !== null,
  }));
  return [...regions, ...cards];
}

function miniatures(doc: CanvasDocument): Map<string, MiniBox[]> {
  const regions = new Map(doc.regions.map((r) => [r.id, r]));
  const result = new Map<string, MiniBox[]>();
  const push = (topId: string, box: MiniBox) => {
    const list = result.get(topId) ?? [];
    list.push(box);
    result.set(topId, list);
  };
  for (const sub of doc.regions) if (sub.parentId) push(sub.parentId, { id: sub.id, x: sub.x, y: sub.y, w: sub.w, h: sub.h, kind: "region" });
  for (const card of doc.cards) {
    const parent = card.regionId ? regions.get(card.regionId) : undefined;
    if (!parent) continue;
    if (parent.parentId) push(parent.parentId, { id: card.id, x: parent.x + card.x, y: parent.y + card.y, w: card.w, h: card.h, kind: "card" });
    else push(parent.id, { id: card.id, x: card.x, y: card.y, w: card.w, h: card.h, kind: "card" });
  }
  return result;
}

export function toEdges(connections: Connection[], level: ZoomLevel): Edge[] {
  return connections.map((connection) => ({
    id: connection.id,
    source: connection.sourceId,
    target: connection.targetId,
    type: "floating",
    className: `edge-${connection.kind}`,
    ...(DIRECTIONAL.has(connection.kind) ? { markerEnd: { type: MarkerType.ArrowClosed, width: 14, height: 14 } } : {}),
    ...(level === "near" && connection.label ? { label: connection.label } : {}),
    data: { kind: connection.kind },
    // Layers (with zIndexMode="manual"): regions 0, sub-regions 1, edges 2, cards 3. Lines never cross card text.
    zIndex: 2,
    selectable: true,
    focusable: true,
  }));
}

/** Absolute canvas position of a node, walking up the parent chain (needed to focus a region or card). */
export function absolute(doc: CanvasDocument, id: string): { x: number; y: number; w: number; h: number } | undefined {
  const regions = new Map(doc.regions.map((r) => [r.id, r]));
  const card = doc.cards.find((c) => c.id === id);
  let item: { x: number; y: number; w: number; h: number; parent: string | null } | undefined = card
    ? { x: card.x, y: card.y, w: card.w, h: card.h, parent: card.regionId }
    : (() => {
        const region = regions.get(id);
        return region ? { x: region.x, y: region.y, w: region.w, h: region.h, parent: region.parentId } : undefined;
      })();
  if (!item) return undefined;
  let { x, y } = item;
  for (let parent = item.parent; parent; parent = regions.get(parent)?.parentId ?? null) {
    const region = regions.get(parent);
    if (!region) break;
    x += region.x;
    y += region.y;
  }
  return { x, y, w: item.w, h: item.h };
}
