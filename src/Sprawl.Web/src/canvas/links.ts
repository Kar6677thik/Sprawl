/**
 * What a link card points to. Recognised by shape only, so no host is built in:
 * - a Slate note: `https://<slate web origin>/?note=<guid>` (the shape Slate's MCP returns as a note's `url`);
 * - a Sprawl canvas or card: `<origin>/c/<cv_…>` with an optional `?focus=<cd_…|rg_…>` (the shape Sprawl's MCP returns);
 * - anything else: a web page.
 * Only http(s) URLs are links at all; the server already refuses other schemes.
 */
export type LinkTarget =
  | { kind: "note"; url: string; host: string; noteId: string }
  | { kind: "canvas"; url: string; host: string; canvasId: string; focus: string | null; here: boolean }
  | { kind: "web"; url: string; host: string };

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const CANVAS_PATH = /^\/c\/(cv_[0-9a-z]{8})\/?$/;
const ITEM = /^(cd|rg)_[0-9a-z]{8}$/;

export function linkTarget(url: string | null | undefined, origin: string = globalThis.location?.origin ?? ""): LinkTarget | null {
  if (!url) return null;
  let parsed: URL;
  try { parsed = new URL(url); } catch { return null; }
  if (parsed.protocol !== "https:" && parsed.protocol !== "http:") return null;
  const host = parsed.host;
  const params = [...parsed.searchParams.keys()];
  const note = parsed.searchParams.get("note");
  if (parsed.protocol === "https:" && (parsed.pathname === "/" || parsed.pathname === "") && note && GUID.test(note) && params.length === 1)
    return { kind: "note", url, host, noteId: note.toLowerCase() };
  const canvas = CANVAS_PATH.exec(parsed.pathname);
  const focus = parsed.searchParams.get("focus");
  if (canvas && params.every((p) => p === "focus") && (focus === null || ITEM.test(focus)))
    return { kind: "canvas", url, host, canvasId: canvas[1]!, focus, here: parsed.origin === origin };
  return { kind: "web", url, host };
}

/** A short label for the link: what it is, then where. */
export function linkLabel(target: LinkTarget): string {
  switch (target.kind) {
    case "note": return "Slate note";
    case "canvas": return target.focus ? (target.focus.startsWith("rg_") ? "Region on a canvas" : "Card on a canvas") : "Canvas";
    default: return target.host;
  }
}
