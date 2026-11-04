import ELK from "elkjs/lib/elk.bundled.js";
import { describe, expect, it } from "vitest";
import { planTidy, tidyItems } from "./tidy";
import { card, documentOf, edge, region } from "./fixtures";

const elk = new ELK();

const doc = documentOf({
  regions: [
    region("rg_00000001", { x: 0, y: 0, w: 920, h: 600 }),
    region("rg_00000002", { x: 1200, y: 40, w: 624, h: 300 }),
    region("rg_00000003", { x: 0, y: 900, w: 624, h: 300, placement: "manual" }),
  ],
  cards: [
    card("cd_00000001", "rg_00000001", { x: 24, y: 48 }),
    card("cd_00000002", "rg_00000001", { x: 400, y: 300, h: 168 }),
    card("cd_00000003", "rg_00000001", { x: 600, y: 48, placement: "manual" }),
    card("cd_00000004", "rg_00000002"),
  ],
  connections: [edge("cx_00000001", "cd_00000001", "cd_00000002"), edge("cx_00000002", "cd_00000002", "cd_00000004")],
});

describe("planTidy", () => {
  it("lays out a region's auto cards, leaving cards placed by hand alone", () => {
    const plan = planTidy(doc, "rg_00000001")!;
    expect(plan.graph.children!.map((c) => c.id)).toEqual(["cd_00000001", "cd_00000002"]);
    expect(plan.graph.layoutOptions!["elk.algorithm"]).toBe("layered"); // the region has an internal connection
    expect(plan.origin).toEqual({ x: 24, y: 48 });
  });

  it("tidies the canvas as top-level regions, with edges aggregated from card connections", () => {
    const plan = planTidy(doc, null)!;
    expect(plan.graph.children!.map((c) => c.id)).toEqual(["rg_00000001", "rg_00000002"]); // rg_3 is manual
    expect(plan.graph.edges).toEqual([{ id: "agg_0", sources: ["rg_00000001"], targets: ["rg_00000002"] }]);
  });

  it("returns nothing when there is too little to arrange", () => {
    expect(planTidy(doc, "rg_00000002")).toBeNull();
  });

  it("produces non-overlapping auto positions in the scope's coordinates", async () => {
    const plan = planTidy(documentOf({
      regions: [region("rg_00000001")],
      cards: Array.from({ length: 7 }, (_, i) => card(`cd_0000000${i}`, "rg_00000001", { h: i % 2 ? 168 : 88 })),
    }), "rg_00000001")!;
    const items = tidyItems(plan, await elk.layout(plan.graph));
    expect(items).toHaveLength(7);
    expect(items.every((item) => item.asAuto && item.x >= 24 && item.y >= 48)).toBe(true);
    const boxes = items.map((item, i) => ({ ...item, w: 280, h: i % 2 ? 168 : 88 }));
    for (const a of boxes)
      for (const b of boxes)
        if (a !== b) expect(a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h).toBe(false);
  });
});
