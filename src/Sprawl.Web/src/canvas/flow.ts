import { MarkerType, type Edge, type Node } from "@xyflow/react";
import { isDiagram, type Card, type CanvasDocument, type Connection, type Region, type Sketch } from "../api/types";
import { orderRegions } from "./document";
import type { ZoomLevel } from "./zoom";
import { sign } from "./loops";

/** A descendant's rectangle relative to its top-level region, drawn as the zoomed-out miniature. */
export type MiniBox = { id: string; x: number; y: number; w: number; h: number; kind: "card" | "region" };
export type RegionNodeData = { region: Region; cardCount: number; minis: MiniBox[]; group: boolean };
export type CardNodeData = { card: Card; fresh: boolean; diagram: boolean; canvasId: string };
export type RegionNode = Node<RegionNodeData, "region">;
export type CardNode = Node<CardNodeData, "card">;
export type SketchNode = Node<{ sketch: Sketch }, "sketch">;
export type SprawlNode = RegionNode | CardNode | SketchNode;

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
  const byId = new Map(doc.regions.map((r) => [r.id, r]));
  const regions: RegionNode[] = orderRegions(doc.regions).map((region) => ({
    id: region.id,
    type: "region",
    position: { x: region.x, y: region.y },
    width: region.w,
    height: region.h,
    ...(region.parentId ? { parentId: region.parentId } : {}),
    data: { region, cardCount: counts.get(region.id) ?? 0, minis: minis.get(region.id) ?? [], group: region.parentId !== null && isDiagram(byId.get(region.parentId)?.layout) },
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
    data: { card, fresh: fresh.has(card.id), diagram: card.regionId !== null && isDiagram(byId.get(card.regionId)?.layout), canvasId: doc.meta.id },
    zIndex: 3,
    hidden: far && card.regionId !== null,
  }));
  // Sketches sit above regions and below cards (layer 2, with edges), so they never hide card text.
  const sketches: SketchNode[] = (doc.sketches ?? []).map((sketch) => ({
    id: sketch.id,
    type: "sketch",
    position: { x: sketch.x, y: sketch.y },
    width: sketch.w,
    height: sketch.h,
    ...(sketch.regionId ? { parentId: sketch.regionId } : {}),
    data: { sketch },
    zIndex: 2,
    hidden: far && sketch.regionId !== null,
  }));
  return [...regions, ...sketches, ...cards];
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

/** Ids of everything inside a diagram (its cards, its groups and their cards): their connections are drawn as diagram edges. */
export function diagramMembers(doc: Pick<CanvasDocument, "regions" | "cards">): Set<string> {
  const byId = new Map(doc.regions.map((r) => [r.id, r]));
  const inDiagram = (regionId: string | null): boolean => {
    for (let id = regionId; id; id = byId.get(id)?.parentId ?? null) if (isDiagram(byId.get(id)?.layout)) return true;
    return false;
  };
  const members = new Set<string>();
  for (const region of doc.regions) if (region.parentId && inDiagram(region.parentId)) members.add(region.id);
  for (const card of doc.cards) if (inDiagram(card.regionId)) members.add(card.id);
  return members;
}

const ARROW = { type: MarkerType.ArrowClosed, width: 14, height: 14 } as const;

/**
 * Connections as edges. Arrowheads follow the connection's ends, or its kind when unset; the line style shows as a dash
 * pattern or weight. Inside diagrams edges run orthogonally and show their labels at every reading zoom.
 */
export function toEdges(connections: Connection[], level: ZoomLevel, members: ReadonlySet<string> = new Set(), loopMembers: ReadonlySet<string> = new Set()): Edge[] {
  return connections.map((connection) => {
    const ends = connection.ends ?? (DIRECTIONAL.has(connection.kind) ? "end" : "none");
    const curved = loopMembers.has(connection.sourceId) && loopMembers.has(connection.targetId);
    const orthogonal = !curved && members.has(connection.sourceId) && members.has(connection.targetId);
    const showLabel = connection.label && (level === "near" || ((orthogonal || curved) && level === "mid"));
    return {
    id: connection.id,
    source: connection.sourceId,
    target: connection.targetId,
    type: "floating",
    className: `edge-${connection.kind} line-${connection.line ?? "solid"}${orthogonal || curved ? " is-diagram" : ""}${curved ? ` is-causal${polarityClass(connection.label)}` : ""}`,
    ...(ends === "end" || ends === "both" ? { markerEnd: ARROW } : {}),
    ...(ends === "start" || ends === "both" ? { markerStart: ARROW } : {}),
    ...(showLabel ? { label: connection.label } : {}),
    data: { kind: connection.kind, orthogonal, curved },
    // Layers (with zIndexMode="manual"): regions 0, sub-regions 1, edges 2, cards 3. Lines never cross card text.
    zIndex: 2,
    selectable: true,
    focusable: true,
    };
  });
}

function polarityClass(label: string | null): string {
  const s = sign(label);
  return s > 0 ? " is-plus" : s < 0 ? " is-minus" : "";
}

/** Ids of the direct members of causal-loop regions: their connections curve around the loop. */
export function loopMembers(doc: Pick<CanvasDocument, "regions" | "cards">): Set<string> {
  const loops = new Set(doc.regions.filter((r) => r.layout === "loops").map((r) => r.id));
  const members = new Set<string>();
  for (const card of doc.cards) if (card.regionId && loops.has(card.regionId)) members.add(card.id);
  for (const region of doc.regions) if (region.parentId && loops.has(region.parentId)) members.add(region.id);
  return members;
}

/** Edge ids for `[[links]]` drawn from card text; they are not connections and cannot be selected or deleted. */
export const LINK_EDGE_PREFIX = "ln:";

/**
 * `[[links]]` in card text, drawn as quiet dotted lines with an arrow. A link that duplicates a connection between the
 * same two cards is not drawn twice. Hidden zoomed out, where cards are not nodes.
 */
export function toLinkEdges(links: [string, string][], connections: Connection[], level: ZoomLevel): Edge[] {
  if (level === "far") return [];
  const connected = new Set(connections.flatMap((c) => [`${c.sourceId}>${c.targetId}`, `${c.targetId}>${c.sourceId}`]));
  return links.filter(([from, to]) => !connected.has(`${from}>${to}`)).map(([from, to]) => ({
    id: `${LINK_EDGE_PREFIX}${from}:${to}`,
    source: from,
    target: to,
    type: "floating",
    className: "edge-text-link",
    markerEnd: { type: MarkerType.Arrow, width: 12, height: 12 },
    zIndex: 2,
    selectable: false,
    focusable: false,
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
