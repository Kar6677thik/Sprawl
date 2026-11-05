import { describe, expect, it } from "vitest";
import { thin, toStroke } from "./ink";

describe("ink", () => {
  it("thins points that barely move but keeps the end", () => {
    expect(thin([{ x: 0, y: 0 }, { x: 0.5, y: 0 }, { x: 3, y: 0 }, { x: 3.5, y: 0 }])).toEqual([{ x: 0, y: 0 }, { x: 3, y: 0 }, { x: 3.5, y: 0 }]);
  });

  it("makes a padded box and a smooth path the server accepts", () => {
    const stroke = toStroke([{ x: 100, y: 100 }, { x: 110, y: 105 }, { x: 120, y: 100 }, { x: 130, y: 110 }], 2)!;
    expect(stroke).toMatchObject({ x: 98, y: 98, w: 34, h: 14 });
    expect(stroke.path).toMatch(/^M[0-9. ]+( [QL][0-9. ]+)+$/);
    expect(stroke.path.startsWith("M2 2 Q12 7")).toBe(true);
  });

  it("ignores a click without movement", () => {
    expect(toStroke([{ x: 5, y: 5 }, { x: 5.2, y: 5 }], 2)).toBeNull();
  });
});
