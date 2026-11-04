import { describe, expect, it } from "vitest";
import { absolute, toEdges, toNodes } from "./flow";
import { card, documentOf, edge, region } from "./fixtures";

const doc = documentOf({
  regions: [region("rg_00000002", { parentId: "rg_00000001", x: 24, y: 152 }), region("rg_00000001", { x: 100, y: 200 })],
  cards: [card("cd_00000001", "rg_00000002", { x: 24, y: 48 }), card("cd_00000002", null, { x: -500, y: 10, placement: "manual" })],
  connections: [edge("cx_00000001", "cd_00000001", "cd_00000002", { kind: "leads-to", label: "because" })],
});

describe("toNodes", () => {
  it("uses server positions with parentId nesting, parents first", () => {
    const nodes = toNodes(doc, new Set(["cd_00000001"]));
    expect(nodes.map((n) => n.id)).toEqual(["rg_00000001", "rg_00000002", "cd_00000001", "cd_00000002"]);
    expect(nodes[1]).toMatchObject({ parentId: "rg_00000001", position: { x: 24, y: 152 }, width: 624, height: 160 });
    expect(nodes[2]).toMatchObject({ parentId: "rg_00000002", type: "card" });
    expect(nodes[3]!.parentId).toBeUndefined();
    expect(nodes[2]!.data).toMatchObject({ fresh: true });
  });

  it("counts cards per region for the zoomed-out view", () => {
    const sub = toNodes(doc).find((n) => n.id === "rg_00000002")!;
    expect(sub.data).toMatchObject({ cardCount: 1 });
  });
});

describe("toNodes zoomed out", () => {
  it("hides cards and sub-regions and gives top-level regions a miniature of their contents", () => {
    const nodes = toNodes(doc, new Set(), "far");
    expect(nodes.filter((n) => !n.hidden).map((n) => n.id)).toEqual(["rg_00000001", "cd_00000002"]); // loose cards stay
    const top = nodes[0]!;
    expect(top.data).toMatchObject({
      minis: [
        { id: "rg_00000002", kind: "region", x: 24, y: 152 },
        { id: "cd_00000001", kind: "card", x: 24 + 24, y: 152 + 48 },
      ],
    });
  });
});

describe("toEdges", () => {
  it("shows labels only up close and arrows only for directional kinds", () => {
    expect(toEdges(doc.connections, "mid")[0]!.label).toBeUndefined();
    const near = toEdges(doc.connections, "near")[0]!;
    expect(near.label).toBe("because");
    expect(near.markerEnd).toBeDefined();
    expect(toEdges([edge("cx_00000002", "a", "b")], "near")[0]!.markerEnd).toBeUndefined();
  });
});

describe("absolute", () => {
  it("adds up parent offsets", () => {
    expect(absolute(doc, "cd_00000001")).toEqual({ x: 100 + 24 + 24, y: 200 + 152 + 48, w: 280, h: 88 });
    expect(absolute(doc, "cd_00000002")).toEqual({ x: -500, y: 10, w: 280, h: 88 });
  });
});
