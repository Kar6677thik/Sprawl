import { describe, expect, it } from "vitest";
import { card, documentOf, edge, region } from "./fixtures";
import { findLoops, loopBadges, sign } from "./loops";

// The same diagram as the server's SystemsTests: Users ↔ Content (R), Users → Load → Latency → Users (B, delayed).
const lead = { kind: "leads-to" as const };
const doc = documentOf({
  regions: [region("rg_00000001", { layout: "loops" })],
  cards: [
    card("cd_00000001", "rg_00000001", { title: "Users", x: 200, y: 0, createdAt: "2026-11-04T12:00:01Z" }),
    card("cd_00000002", "rg_00000001", { title: "Content", x: 400, y: 200, createdAt: "2026-11-04T12:00:02Z" }),
    card("cd_00000003", "rg_00000001", { title: "Load", x: 200, y: 400, createdAt: "2026-11-04T12:00:03Z" }),
    card("cd_00000004", "rg_00000001", { title: "Latency", x: 0, y: 200, createdAt: "2026-11-04T12:00:04Z" }),
  ],
  connections: [
    edge("cx_00000001", "cd_00000001", "cd_00000002", { ...lead, label: "+" }),
    edge("cx_00000002", "cd_00000002", "cd_00000001", { ...lead, label: "+" }),
    edge("cx_00000003", "cd_00000001", "cd_00000003", { ...lead, label: "+" }),
    edge("cx_00000004", "cd_00000003", "cd_00000004", { ...lead, label: "+" }),
    edge("cx_00000005", "cd_00000004", "cd_00000001", { ...lead, label: "- delay" }),
  ],
});

describe("causal loops", () => {
  it("are found, signed and named as the server names them", () => {
    const loops = findLoops(doc, "rg_00000001");
    expect(loops.map((l) => [l.name, l.polarity, l.delayed, l.members])).toEqual([
      ["B1", "B", true, ["cd_00000001", "cd_00000003", "cd_00000004"]],
      ["R1", "R", false, ["cd_00000001", "cd_00000002"]],
    ]);
  });

  it("read polarity from the label", () => {
    expect([sign("+"), sign("−"), sign("- ||"), sign("o"), sign("causes"), sign(null)]).toEqual([1, -1, -1, -1, 0, 0]);
  });

  it("get a badge at the middle of their members, turning the way they run", () => {
    const [b1] = loopBadges(doc).get("rg_00000001")!;
    expect(b1!.name).toBe("B1");
    // Users (12 o'clock) → Load (6) → Latency (9) → Users: on screen that turns clockwise.
    expect(b1!.clockwise).toBe(true);
    expect(Math.round(b1!.x)).toBe(Math.round((340 + 340 + 140) / 3));
  });
});
