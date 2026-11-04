import type { ElkExtendedEdge, ElkNode } from "elkjs/lib/elk-api";
import type { CanvasDocument, LayoutItem } from "../api/types";

export const PADDING = 24;
export const HEADER = 48;
const CARD_GAP = 16;
const REGION_GAP = 64;

export interface TidyPlan {
  /** The region being tidied, or null for the whole canvas (top-level regions). */
  scope: string | null;
  graph: ElkNode;
  /** Where the laid-out graph's (0, 0) lands, in the scope's coordinate space. */
  origin: { x: number; y: number };
}

/**
 * Build the ELK graph for a Tidy. Items placed by hand are left out unless `includeManual`, and stay where they are; the
 * server pushes any auto item that would overlap one. Layered layout when connections show a flow, rect packing otherwise.
 */
export function planTidy(doc: CanvasDocument, scope: string | null, includeManual = false): TidyPlan | null {
  const keep = (placement: string) => includeManual || placement === "auto";
  const children: { id: string; width: number; height: number }[] = [];
  const edges: ElkExtendedEdge[] = [];
  if (scope) {
    for (const card of doc.cards) if (card.regionId === scope && keep(card.placement)) children.push({ id: card.id, width: card.w, height: card.h });
    for (const sub of doc.regions) if (sub.parentId === scope && keep(sub.placement)) children.push({ id: sub.id, width: sub.w, height: sub.h });
    const ids = new Set(children.map((c) => c.id));
    for (const c of doc.connections) if (ids.has(c.sourceId) && ids.has(c.targetId)) edges.push({ id: c.id, sources: [c.sourceId], targets: [c.targetId] });
  } else {
    for (const region of doc.regions) if (!region.parentId && keep(region.placement)) children.push({ id: region.id, width: region.w, height: region.h });
    const ids = new Set(children.map((c) => c.id));
    const top = topLevelRegionOf(doc);
    const seen = new Set<string>();
    for (const c of doc.connections) {
      const a = top(c.sourceId);
      const b = top(c.targetId);
      if (!a || !b || a === b || !ids.has(a) || !ids.has(b) || seen.has(`${a}>${b}`)) continue;
      seen.add(`${a}>${b}`);
      edges.push({ id: `agg_${edges.length}`, sources: [a], targets: [b] });
    }
  }
  if (children.length < 2) return null;
  const gap = scope ? CARD_GAP : REGION_GAP;
  const layered = edges.length > 0;
  const graph: ElkNode = {
    id: "root",
    layoutOptions: {
      "elk.algorithm": layered ? "layered" : "rectpacking",
      "elk.direction": scope ? "DOWN" : "RIGHT",
      "elk.spacing.nodeNode": String(gap),
      "elk.layered.spacing.nodeNodeBetweenLayers": String(gap * 2),
      "elk.layered.nodePlacement.strategy": "BRANDES_KOEPF",
      "elk.aspectRatio": scope ? "1.2" : "1.6",
      "elk.padding": "[top=0,left=0,bottom=0,right=0]",
    },
    children,
    edges,
  };
  const origin = scope ? { x: PADDING, y: HEADER } : topLeft(doc, children.map((c) => c.id));
  return { scope, graph, origin };
}

/** Turn ELK's output into a tidy batch: auto-placed layout items, positions in the scope's coordinates. */
export function tidyItems(plan: TidyPlan, result: ElkNode): LayoutItem[] {
  return (result.children ?? []).map((node) => ({
    id: node.id,
    x: Math.round((node.x ?? 0) + plan.origin.x),
    y: Math.round((node.y ?? 0) + plan.origin.y),
    asAuto: true,
  }));
}

function topLevelRegionOf(doc: CanvasDocument) {
  const regions = new Map(doc.regions.map((r) => [r.id, r]));
  const cards = new Map(doc.cards.map((c) => [c.id, c]));
  return (id: string): string | null => {
    const regionId = cards.has(id) ? cards.get(id)!.regionId : regions.has(id) ? id : null;
    if (!regionId) return null;
    return regions.get(regionId)?.parentId ?? regionId;
  };
}

function topLeft(doc: CanvasDocument, ids: string[]) {
  const rects = doc.regions.filter((r) => ids.includes(r.id));
  return { x: Math.min(...rects.map((r) => r.x)), y: Math.min(...rects.map((r) => r.y)) };
}

let worker: Worker | null = null;
let sequence = 0;

/** Load and parse ELK in the background so the first Tidy is instant (the worker bundle is large). */
export function warmTidy() {
  void layout({ id: "warm", children: [{ id: "a", width: 1, height: 1 }, { id: "b", width: 1, height: 1 }] }).catch(() => undefined);
}

/** Run ELK off the main thread (a module worker bundled by Vite), so a large Tidy never janks the canvas. */
export function layout(graph: ElkNode): Promise<ElkNode> {
  worker ??= new Worker(new URL("./tidy.worker.ts", import.meta.url), { type: "module" });
  const id = ++sequence;
  const current = worker;
  return new Promise((resolve, reject) => {
    const onMessage = (event: MessageEvent<{ id: number; result?: ElkNode; error?: string }>) => {
      if (event.data.id !== id) return;
      current.removeEventListener("message", onMessage);
      if (event.data.result) resolve(event.data.result);
      else reject(new Error(event.data.error ?? "Tidy failed."));
    };
    current.addEventListener("message", onMessage);
    current.postMessage({ id, graph });
  });
}
