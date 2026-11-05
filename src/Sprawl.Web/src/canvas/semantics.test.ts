import { describe, expect, it } from "vitest";
import { card, documentOf, edge } from "./fixtures";
import { toLinkEdges } from "./flow";
import { linkFirstMention, LinkIndex, linkify, linkTargets, normalizeTitle, tagsOf } from "./semantics";

describe("semantics", () => {
  it("reads links and tags from prose, never from code", () => {
    expect(linkTargets("See [[Leader election]] and [[cd_00000009|that]], `[[no]]`\n```\n[[nor]]\n```")).toEqual(["Leader election", "cd_00000009"]);
    expect(tagsOf({ title: "Timeouts #raft", body: "For #Liveness.\n# Heading\nhttps://x.io/#anchor `#include` #2026" })).toEqual(["raft", "liveness"]);
  });

  it("normalizes titles like the server", () => {
    expect(normalizeTitle("  Leader   Election?! ")).toBe("leader election");
  });

  it("resolves links by id or title, oldest card first, and skips links that duplicate connections", () => {
    const doc = documentOf({
      cards: [
        card("cd_00000001", null, { title: "Votes", createdAt: "2026-11-01T00:00:00Z" }),
        card("cd_00000002", null, { title: "votes", createdAt: "2026-11-02T00:00:00Z" }),
        card("cd_00000003", null, { title: "Terms", body: "Uses [[Votes.]] and [[cd_00000002]] and [[Missing]] and [[Votes]] again." }),
      ],
    });
    const index = new LinkIndex(doc);
    expect(index.resolve("VOTES")?.id).toBe("cd_00000001");
    expect(index.links(doc)).toEqual([["cd_00000003", "cd_00000001"], ["cd_00000003", "cd_00000002"]]);
    const edges = toLinkEdges(index.links(doc), [edge("cx_00000001", "cd_00000002", "cd_00000003")], "mid");
    expect(edges.map((e) => e.id)).toEqual(["ln:cd_00000003:cd_00000001"]);
    expect(toLinkEdges(index.links(doc), [], "far")).toEqual([]);
  });

  it("turns links and tags into fragment links, leaving code alone", () => {
    expect(linkify("See [[Leader election|the election]] #raft `[[code]] #x`")).toBe(
      "See [the election](#sprawl-link/Leader%20election) [#raft](#sprawl-tag/raft) `[[code]] #x`");
  });

  it("links the first plain mention, or appends a link", () => {
    expect(linkFirstMention("Snapshots are how log compaction works.", "Log compaction")).toBe("Snapshots are how [[log compaction]] works.");
    expect(linkFirstMention("Nothing", "Log compaction")).toBe("Nothing [[Log compaction]]");
  });
});
