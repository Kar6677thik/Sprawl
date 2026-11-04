import { describe, expect, it } from "vitest";
import { parseRoute } from "./router";

describe("parseRoute", () => {
  it("recognises canvas routes by id shape and sends everything else home", () => {
    expect(parseRoute("/c/cv_3f9k2a7q")).toEqual({ name: "canvas", id: "cv_3f9k2a7q" });
    expect(parseRoute("/c/cv_3f9k2a7q/")).toEqual({ name: "canvas", id: "cv_3f9k2a7q" });
    expect(parseRoute("/c/not-an-id")).toEqual({ name: "home" });
    expect(parseRoute("/")).toEqual({ name: "home" });
  });
});
