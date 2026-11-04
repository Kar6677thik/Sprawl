import { describe, expect, it } from "vitest";
import { applyEvent, canRebase, moveLocally, orderRegions } from "./document";
import { card, documentOf, edge, eventOf, region } from "./fixtures";

describe("applyEvent", () => {
  const doc = documentOf({ regions: [region("rg_00000001")], cards: [card("cd_00000001", "rg_00000001")] });

  it("applies the next version's after-images: creates, updates and deletes", () => {
    const result = applyEvent(doc, eventOf(4, [
      { type: "card.create", entityId: "cd_00000002", before: null, after: card("cd_00000002", "rg_00000001", { title: "new" }) },
      { type: "region.layout", entityId: "rg_00000001", before: null, after: region("rg_00000001", { h: 300 }) },
      { type: "card.delete", entityId: "cd_00000001", before: null, after: null },
      { type: "connection.create", entityId: "cx_00000001", before: null, after: edge("cx_00000001", "cd_00000002", "rg_00000001") },
    ]));
    expect(result.status).toBe("applied");
    if (result.status !== "applied") return;
    expect(result.doc.version).toBe(4);
    expect(result.doc.cards.map((c) => c.id)).toEqual(["cd_00000002"]);
    expect(result.doc.regions[0]!.h).toBe(300);
    expect(result.doc.connections).toHaveLength(1);
    expect(result.added).toEqual(["cd_00000002"]);
  });

  it("ignores versions it already has (its own writes, replays)", () => {
    expect(applyEvent(doc, eventOf(3, [])).status).toBe("ignored");
    expect(applyEvent(doc, eventOf(2, [])).status).toBe("ignored");
  });

  it("reports a gap when events were missed, so the caller refetches", () => {
    expect(applyEvent(doc, eventOf(6, []))).toEqual({ status: "gap", expected: 4, received: 6 });
  });

  it("updates canvas metadata", () => {
    const result = applyEvent(doc, eventOf(4, [{ type: "canvas.update", entityId: "cv_00000001", before: null, after: { ...doc.meta, title: "Raft, revised" } }]));
    expect(result.status === "applied" && result.doc.meta.title).toBe("Raft, revised");
  });
});

describe("orderRegions", () => {
  it("puts parents before sub-regions, as React Flow requires", () => {
    const ordered = orderRegions([region("rg_00000002", { parentId: "rg_00000001" }), region("rg_00000001")]);
    expect(ordered.map((r) => r.id)).toEqual(["rg_00000001", "rg_00000002"]);
  });
});

describe("canRebase", () => {
  const base = documentOf({ cards: [card("cd_00000001", "rg_00000001"), card("cd_00000002", "rg_00000001")] });

  it("allows a replay when only other entities changed", () => {
    const fresh = { ...base, version: 5, cards: [base.cards[0]!, { ...base.cards[1]!, title: "edited by Claude" }] };
    expect(canRebase(base, fresh, ["cd_00000001"])).toBe(true);
  });

  it("refuses when a touched entity changed or disappeared", () => {
    expect(canRebase(base, { ...base, cards: [{ ...base.cards[0]!, y: 400 }, base.cards[1]!] }, ["cd_00000001"])).toBe(false);
    expect(canRebase(base, { ...base, cards: [base.cards[1]!] }, ["cd_00000001"])).toBe(false);
  });
});

describe("moveLocally", () => {
  it("moves and pins the dragged items only", () => {
    const doc = documentOf({ cards: [card("cd_00000001", "rg_00000001"), card("cd_00000002", "rg_00000001")] });
    const moved = moveLocally(doc, [{ id: "cd_00000002", x: 400, y: 300 }]);
    expect(moved.cards[1]).toMatchObject({ x: 400, y: 300, placement: "manual" });
    expect(moved.cards[0]).toBe(doc.cards[0]);
  });
});

describe("moveLocally into another container", () => {
  it("reparents a card dropped into a region, and makes a card dropped on open canvas loose and manual", () => {
    const doc = documentOf({ cards: [card("cd_00000001", "rg_00000001"), card("cd_00000002", "rg_00000001")] });
    const moved = moveLocally(doc, [
      { id: "cd_00000001", x: 30, y: 60, container: "rg_00000002" },
      { id: "cd_00000002", x: -400, y: 10, container: "canvas", asAuto: true },
    ]);
    expect(moved.cards[0]).toMatchObject({ regionId: "rg_00000002", x: 30, y: 60, placement: "manual" });
    expect(moved.cards[1]).toMatchObject({ regionId: null, placement: "manual" });
  });
});
