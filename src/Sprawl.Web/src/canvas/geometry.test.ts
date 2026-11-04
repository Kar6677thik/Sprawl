import { describe, expect, it } from "vitest";
import { floatingEndpoints, regionAt, relativeTo } from "./geometry";
import { documentOf, region } from "./fixtures";

describe("floatingEndpoints", () => {
  const a = { x: 0, y: 0, w: 100, h: 50 };

  it("attaches to the facing sides", () => {
    const right = floatingEndpoints(a, { x: 300, y: 0, w: 100, h: 50 });
    expect(right).toMatchObject({ sx: 100, sy: 25, sourceSide: "right", tx: 300, ty: 25, targetSide: "left" });
    const below = floatingEndpoints(a, { x: 0, y: 300, w: 100, h: 50 });
    expect(below).toMatchObject({ sourceSide: "bottom", sy: 50, targetSide: "top", ty: 300 });
  });

  it("leaves the border along the line between centres for diagonal neighbours", () => {
    const { sx, sy, sourceSide } = floatingEndpoints(a, { x: 400, y: 400, w: 100, h: 50 });
    expect(sourceSide).toBe("bottom");
    expect(sy).toBe(50);
    expect(sx).toBeGreaterThan(50);
    expect(sx).toBeLessThan(100);
  });
});

describe("regionAt", () => {
  const doc = documentOf({
    regions: [region("rg_00000001", { x: 100, y: 100, w: 800, h: 600 }), region("rg_00000002", { parentId: "rg_00000001", x: 24, y: 200, w: 400, h: 200 })],
  });

  it("finds the innermost region under a point", () => {
    expect(regionAt(doc, { x: 150, y: 150 })).toBe("rg_00000001");
    expect(regionAt(doc, { x: 200, y: 350 })).toBe("rg_00000002");
    expect(regionAt(doc, { x: 50, y: 50 })).toBeNull();
  });

  it("expresses canvas points relative to the region they land in", () => {
    expect(relativeTo(doc, "rg_00000002", { x: 200.4, y: 350 })).toEqual({ x: 76, y: 50 });
    expect(relativeTo(doc, null, { x: -10.6, y: 4 })).toEqual({ x: -11, y: 4 });
  });
});
