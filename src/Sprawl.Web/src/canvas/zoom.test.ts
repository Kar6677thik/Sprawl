import { describe, expect, it } from "vitest";
import { levelFor } from "./zoom";

describe("levelFor", () => {
  it("maps zoom to the three semantic levels", () => {
    expect(levelFor(0.2)).toBe("far");
    expect(levelFor(0.5)).toBe("mid");
    expect(levelFor(1.2)).toBe("near");
  });

  it("holds a level until the zoom passes the boundary by the hysteresis margin", () => {
    expect(levelFor(0.36, "far")).toBe("far");
    expect(levelFor(0.4, "far")).toBe("mid");
    expect(levelFor(0.33, "mid")).toBe("mid");
    expect(levelFor(0.3, "mid")).toBe("far");
    expect(levelFor(0.79, "near")).toBe("near");
    expect(levelFor(0.75, "near")).toBe("mid");
    expect(levelFor(0.82, "mid")).toBe("mid");
    expect(levelFor(0.85, "mid")).toBe("near");
  });

  it("jumps straight across when the zoom moves far in one step", () => {
    expect(levelFor(1.5, "far")).toBe("near");
    expect(levelFor(0.1, "near")).toBe("far");
  });
});
