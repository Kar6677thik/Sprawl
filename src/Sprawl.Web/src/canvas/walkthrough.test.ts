import { describe, expect, it } from "vitest";
import { card, documentOf, edge, region } from "./fixtures";
import { litFor } from "./walkthrough";

const doc = documentOf({
  regions: [region("rg_00000001", { layout: "flow-right" }), region("rg_00000002", { parentId: "rg_00000001" })],
  cards: [card("cd_00000001", "rg_00000001"), card("cd_00000002", "rg_00000001"), card("cd_00000003", "rg_00000002"), card("cd_00000004", "rg_00000001")],
  connections: [
    edge("cx_00000001", "cd_00000001", "cd_00000002"),
    edge("cx_00000002", "cd_00000002", "cd_00000003"),
    edge("cx_00000003", "cd_00000003", "cd_00000004"),
  ],
});

describe("walkthrough steps", () => {
  it("light the named cards and the connections between them", () => {
    const lit = litFor(doc, ["cd_00000001", "cd_00000002"]);
    expect([...lit.nodes].sort()).toEqual(["cd_00000001", "cd_00000002"]);
    expect([...lit.edges]).toEqual(["cx_00000001"]);
  });

  it("light a group's contents when the group is named, and a named connection's ends", () => {
    const group = litFor(doc, ["rg_00000002", "cd_00000002"]);
    expect([...group.nodes].sort()).toEqual(["cd_00000002", "cd_00000003", "rg_00000002"]);
    expect([...group.edges]).toEqual(["cx_00000002"]);
    const named = litFor(doc, ["cx_00000003"]);
    expect([...named.nodes].sort()).toEqual(["cd_00000003", "cd_00000004"]);
  });
});
