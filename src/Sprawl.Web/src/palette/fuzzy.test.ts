import { describe, expect, it } from "vitest";
import { rank, score } from "./fuzzy";

describe("fuzzy ranking", () => {
  it("requires every character in order", () => {
    expect(score("lel", "Leader election")).not.toBeNull();
    expect(score("xyz", "Leader election")).toBeNull();
  });

  it("prefers word starts and prefixes over scattered matches", () => {
    const titles = ["Ballet notes", "Leader election", "Log replication", "Lease-based reads"];
    expect(rank("le", titles, (t) => t)[0]).toBe("Leader election");
    expect(rank("lr", titles, (t) => t)[0]).toBe("Log replication");
    expect(rank("tidy", ["Tidy canvas", "Tidy selected region", "Zoom: titles"], (t) => t)).toEqual(["Tidy canvas", "Tidy selected region"]);
  });

  it("returns everything for an empty query, in input order", () => {
    expect(rank("", ["b", "a"], (t) => t)).toEqual(["b", "a"]);
  });
});
