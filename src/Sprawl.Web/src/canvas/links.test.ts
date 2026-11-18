import { describe, expect, it } from "vitest";
import { linkLabel, linkTarget } from "./links";

const here = "https://sprawl.example.net";

describe("link targets", () => {
  it("recognise a Slate note by its shape, whatever the host", () => {
    const target = linkTarget("https://lib.example.org/?note=3F2504E0-4F89-11D3-9A0C-0305E82C3301", here);
    expect(target).toMatchObject({ kind: "note", host: "lib.example.org", noteId: "3f2504e0-4f89-11d3-9a0c-0305e82c3301" });
    expect(linkLabel(target!)).toBe("Slate note");
  });

  it("do not mistake near misses for notes", () => {
    expect(linkTarget("http://lib.example.org/?note=3f2504e0-4f89-11d3-9a0c-0305e82c3301", here)?.kind).toBe("web");
    expect(linkTarget("https://lib.example.org/?note=42", here)?.kind).toBe("web");
    expect(linkTarget("https://lib.example.org/notes?note=3f2504e0-4f89-11d3-9a0c-0305e82c3301", here)?.kind).toBe("web");
    expect(linkTarget("https://lib.example.org/?note=3f2504e0-4f89-11d3-9a0c-0305e82c3301&x=1", here)?.kind).toBe("web");
  });

  it("recognise Sprawl canvases and cards, here or elsewhere", () => {
    expect(linkTarget(`${here}/c/cv_ab12cd34?focus=cd_00000001`, here)).toMatchObject({ kind: "canvas", canvasId: "cv_ab12cd34", focus: "cd_00000001", here: true });
    const elsewhere = linkTarget("https://other.example.net/c/cv_ab12cd34", here);
    expect(elsewhere).toMatchObject({ kind: "canvas", focus: null, here: false });
    expect(linkLabel(elsewhere!)).toBe("Canvas");
    expect(linkLabel(linkTarget(`${here}/c/cv_ab12cd34?focus=rg_00000001`, here)!)).toBe("Region on a canvas");
  });

  it("treat everything else as a web page, and refuse other schemes", () => {
    expect(linkLabel(linkTarget("https://en.wikipedia.org/wiki/Raft", here)!)).toBe("en.wikipedia.org");
    expect(linkTarget("javascript:alert(1)", here)).toBeNull();
    expect(linkTarget("not a url", here)).toBeNull();
  });
});
