import type { Card, CanvasDocument, CanvasEvent, Connection, Region } from "../api/types";

const at = "2026-11-04T12:00:00Z";

export function region(id: string, overrides: Partial<Region> = {}): Region {
  return { id, parentId: null, title: id, tint: null, cols: 2, x: 0, y: 0, w: 624, h: 160, placement: "auto", createdBy: "ai", createdAt: at, updatedAt: at, ...overrides };
}

export function card(id: string, regionId: string | null, overrides: Partial<Card> = {}): Card {
  return {
    id, regionId, kind: "idea", title: id, body: "", url: null, language: null, tint: null, emphasis: "normal", size: "compact",
    x: 24, y: 48, w: 280, h: 88, placement: "auto", sizeSource: "auto", createdBy: "ai", updatedBy: "ai", createdAt: at, updatedAt: at, ...overrides,
  };
}

export function edge(id: string, sourceId: string, targetId: string, overrides: Partial<Connection> = {}): Connection {
  return { id, sourceId, targetId, kind: "relates", label: null, createdBy: "ai", createdAt: at, ...overrides };
}

export function documentOf(parts: Partial<CanvasDocument> = {}): CanvasDocument {
  return {
    meta: { id: "cv_00000001", title: "Raft", description: "", tags: [], collection: null, pinned: false, archived: false, createdAt: at },
    version: 3, updatedAt: at, regions: [], cards: [], connections: [], layoutWarnings: [], ...parts,
  };
}

export function eventOf(version: number, ops: CanvasEvent["ops"], actor: CanvasEvent["actor"] = "mcp"): CanvasEvent {
  return { canvasId: "cv_00000001", version, kind: "edit", actor, client: null, summary: "", ops, createdAt: at };
}
