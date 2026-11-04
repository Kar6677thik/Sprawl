import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "../api/client";
import type { CanvasDocument, CanvasEvent, Intent, LayoutItem } from "../api/types";
import { applyEvent, canRebase, moveLocally } from "./document";

const FRESH_MS = 2400;

export interface CanvasNotice {
  id: number;
  text: string;
  focus?: string;
}

/**
 * One open canvas: the document, kept live from SSE (Claude's edits appear without a reload), plus the owner's writes.
 * Writes are optimistic; the server's own event for the batch then replaces local state with the authoritative layout.
 */
export function useCanvas(id: string) {
  const [doc, setDoc] = useState<CanvasDocument | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fresh, setFresh] = useState<ReadonlySet<string>>(new Set());
  const [notice, setNotice] = useState<CanvasNotice | null>(null);
  const [live, setLive] = useState(false);
  const docRef = useRef<CanvasDocument | null>(null);
  const lastWrite = useRef(0);
  const noticeId = useRef(0);

  const commit = useCallback((next: CanvasDocument) => {
    docRef.current = next;
    setDoc(next);
  }, []);

  const say = useCallback((text: string, focus?: string) => setNotice({ id: ++noticeId.current, text, ...(focus ? { focus } : {}) }), []);

  const reload = useCallback(async () => {
    try {
      commit(await api.canvas(id));
      setError(null);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The canvas could not be loaded.");
    }
  }, [commit, id]);

  const markFresh = useCallback((ids: string[]) => {
    if (ids.length === 0) return;
    setFresh((current) => new Set([...current, ...ids]));
    window.setTimeout(() => setFresh((current) => new Set([...current].filter((x) => !ids.includes(x)))), FRESH_MS);
  }, []);

  const onEvent = useCallback((event: CanvasEvent) => {
    const current = docRef.current;
    if (!current) return;
    const result = applyEvent(current, event);
    if (result.status === "gap") { void reload(); return; }
    if (result.status !== "applied") return;
    commit(result.doc);
    if (event.actor === "mcp" && result.added.length > 0) {
      markFresh(result.added);
      const regionIds = new Set(result.added.map((cardId) => result.doc.cards.find((c) => c.id === cardId)?.regionId));
      const only = regionIds.size === 1 ? [...regionIds][0] : undefined;
      const where = only ? ` to ${result.doc.regions.find((r) => r.id === only)?.title ?? "a region"}` : "";
      say(`Claude added ${result.added.length} card${result.added.length === 1 ? "" : "s"}${where}`, only ?? result.added[0]);
    }
  }, [commit, markFresh, reload, say]);

  useEffect(() => {
    let source: EventSource | null = null;
    let cancelled = false;
    void (async () => {
      await reload();
      if (cancelled || !docRef.current) return;
      // Start the stream at the version we loaded, so nothing committed in between is lost.
      source = new EventSource(`${api.eventsUrl(id)}?since=${docRef.current.version}`, { withCredentials: true });
      source.addEventListener("open", () => setLive(true));
      source.addEventListener("error", () => setLive(false));
      source.addEventListener("batch", (message) => onEvent(JSON.parse((message as MessageEvent<string>).data) as CanvasEvent));
      source.addEventListener("reset", () => void reload());
    })();
    return () => {
      cancelled = true;
      source?.close();
    };
  }, [id, onEvent, reload]);

  /** Send intents based on the current document. On 412, replay once if nothing we touched changed meanwhile. */
  const write = useCallback(async (intents: Intent[], touched: string[], kind?: "edit" | "tidy") => {
    const base = docRef.current;
    if (!base) return;
    const send = (version: number) => api.batch(id, kind ? { intents, kind } : { intents }, version);
    try {
      const result = await send(Math.max(base.version, lastWrite.current));
      lastWrite.current = result.version;
      // If the SSE event for our own batch does not arrive promptly (stream reconnecting), fetch the truth instead.
      window.setTimeout(() => { if ((docRef.current?.version ?? 0) < result.version) void reload(); }, 1500);
      return result;
    } catch (failure) {
      if (failure instanceof ApiError && failure.code === "stale_version") {
        const latest = await api.canvas(id);
        if (canRebase(base, latest, touched)) {
          commit(latest);
          const result = await send(latest.version);
          lastWrite.current = result.version;
          return result;
        }
        commit(latest);
        say("The canvas changed while you were editing; your change was not applied.");
        return;
      }
      await reload();
      say(failure instanceof Error ? failure.message : "The change could not be saved.");
      return;
    }
  }, [commit, id, reload, say]);

  /** The owner dragged items: they become manual (pinned) at the dropped position. */
  const move = useCallback(async (items: LayoutItem[]) => {
    const current = docRef.current;
    if (!current || items.length === 0) return;
    commit(moveLocally(current, items));
    await write([{ type: "setLayout", items }], items.map((item) => item.id));
  }, [commit, write]);

  return { doc, error, fresh, notice, live, reload, write, move, dismissNotice: () => setNotice(null) };
}
