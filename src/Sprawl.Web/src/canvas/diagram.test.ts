import { describe, expect, it } from "vitest";
import { card, documentOf, edge, region } from "./fixtures";
import { diagramMembers, toEdges, toNodes } from "./flow";

const doc = documentOf({
  regions: [
    region("rg_00000001", { layout: "flow-right" }),
    region("rg_00000002", { parentId: "rg_00000001" }),
    region("rg_00000003"),
  ],
  cards: [
    card("cd_00000001", "rg_00000001", { shape: "diamond", w: 144, h: 88 }),
    card("cd_00000002", "rg_00000002", { shape: "cylinder" }),
    card("cd_00000003", "rg_00000003"),
  ],
  connections: [
    edge("cx_00000001", "cd_00000001", "cd_00000002", { kind: "leads-to", line: "dashed", label: "async" }),
    edge("cx_00000002", "cd_00000003", "cd_00000001", { kind: "relates", ends: "both" }),
  ],
});

describe("diagrams", () => {
  it("knows what is inside a diagram, groups included", () => {
    expect([...diagramMembers(doc)].sort()).toEqual(["cd_00000001", "cd_00000002", "rg_00000002"]);
  });

  it("draws diagram edges orthogonally with their style, arrowheads and labels at reading zoom", () => {
    const [inside, across] = toEdges(doc.connections, "mid", diagramMembers(doc));
    expect(inside).toMatchObject({ className: "edge-leads-to line-dashed is-diagram", label: "async", data: { orthogonal: true } });
    expect(inside!.markerEnd).toBeDefined();
    expect(inside!.markerStart).toBeUndefined();
    expect(across).toMatchObject({ data: { orthogonal: false } });
    expect(across!.markerStart).toBeDefined();
    expect(across!.markerEnd).toBeDefined();
    expect(across!.label).toBeUndefined();
  });

  it("marks diagram cards and groups so they get connection ports", () => {
    const nodes = toNodes(doc);
    expect(nodes.find((n) => n.id === "cd_00000001")!.data).toMatchObject({ diagram: true });
    expect(nodes.find((n) => n.id === "cd_00000003")!.data).toMatchObject({ diagram: false });
    expect(nodes.find((n) => n.id === "rg_00000002")!.data).toMatchObject({ group: true });
  });
});
