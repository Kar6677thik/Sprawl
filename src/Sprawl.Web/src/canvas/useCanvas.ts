import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "../api/client";
import type { BatchResult, CanvasDocument, CanvasEvent, Intent, LayoutItem } from "../api/types";
import { applyEvent, canRebase, moveLocally } from "./document";
import { layout, planTidy, tidyItems } from "./tidy";
import { afterRedo, afterUndo, dropRedo, dropUndo, emptyUndo, nextRedo, nextUndo, recordEdit, type UndoState } from "./undo";

const FRESH_MS = 2400;

export interface CanvasNotice {
  id: number;
  text: string;
  focus?: string;
  action?: { label: string; run: () => void };
}

/**
 * One open canvas: the document, kept live from SSE (Claude's edits appear without a reload), plus the owner's writes.
 * Writes are optimistic where it matters (drags); the server's own event for each batch then replaces local state with
 * the authoritative layout. Every edit the owner makes is undoable with Ctrl+Z (a selective undo on the server).
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
  const history = useRef<UndoState>(emptyUndo);

  const commit = useCallback((next: CanvasDocument) => {
    docRef.current = next;
    setDoc(next);
  }, []);

  const say = useCallback((text: string, extra: Omit<CanvasNotice, "id" | "text"> = {}) => setNotice({ id: ++noticeId.current, text, ...extra }), []);

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
      say(`Claude added ${result.added.length} card${result.added.length === 1 ? "" : "s"}${where}`, { focus: only ?? result.added[0]! });
    }
  }, [commit, markFresh, reload, say]);

  useEffect(() => {
    let source: EventSource | null = null;
    let cancelled = false;
    history.current = emptyUndo;
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

  /** If the SSE event for a batch does not arrive promptly (stream reconnecting), fetch the truth instead. */
  const expect = useCallback((version: number) => {
    lastWrite.current = Math.max(lastWrite.current, version);
    window.setTimeout(() => { if ((docRef.current?.version ?? 0) < version) void reload(); }, 1500);
  }, [reload]);

  /** Send intents based on the current document. On 412, replay once if nothing we touched changed meanwhile. */
  const write = useCallback(async (intents: Intent[], touched: string[], kind?: "edit" | "tidy"): Promise<BatchResult | undefined> => {
    const base = docRef.current;
    if (!base) return;
    const send = (version: number) => api.batch(id, kind ? { intents, kind } : { intents }, version);
    const done = (result: BatchResult) => {
      if (result.changed) {
        expect(result.version);
        history.current = recordEdit(history.current, result.version);
      }
      return result;
    };
    try {
      return done(await send(Math.max(base.version, lastWrite.current)));
    } catch (failure) {
      if (failure instanceof ApiError && failure.code === "stale_version") {
        const latest = await api.canvas(id);
        commit(latest);
        if (canRebase(base, latest, touched)) return done(await send(latest.version));
        say("The canvas changed while you were editing; your change was not applied.");
        return;
      }
      await reload();
      say(failure instanceof Error ? failure.message : "The change could not be saved.");
      return;
    }
  }, [commit, expect, id, reload, say]);

  /** The owner dragged items: they become manual (pinned) at the dropped position, possibly in a new container. */
  const move = useCallback(async (items: LayoutItem[]) => {
    const current = docRef.current;
    if (!current || items.length === 0) return;
    commit(moveLocally(current, items));
    await write([{ type: "setLayout", items }], items.map((item) => item.id));
  }, [commit, write]);

  const undoVersion = useCallback(async (version: number, redo = false) => {
    try {
      const result = await api.undo(id, version);
      expect(result.version);
      history.current = redo ? afterRedo(history.current, result.version) : afterUndo(history.current, result.version);
      say(result.summary);
    } catch (failure) {
      history.current = redo ? dropRedo(history.current) : dropUndo(history.current);
      say(failure instanceof Error ? failure.message : "That could not be undone.");
    }
  }, [expect, id, say]);

  const undo = useCallback(async () => {
    const version = nextUndo(history.current);
    if (version === undefined) { say("Nothing to undo."); return; }
    await undoVersion(version);
  }, [say, undoVersion]);

  const redo = useCallback(async () => {
    const version = nextRedo(history.current);
    if (version === undefined) { say("Nothing to redo."); return; }
    await undoVersion(version, true);
  }, [say, undoVersion]);

  /** Tidy a region (or the whole canvas) with ELK. Never automatic; one undoable batch. */
  const tidy = useCallback(async (scope: string | null) => {
    const current = docRef.current;
    if (!current) return;
    const plan = planTidy(current, scope);
    if (!plan) { say("Nothing to tidy here."); return; }
    try {
      const items = tidyItems(plan, await layout(plan.graph));
      const result = await write([{ type: "setLayout", items }], items.map((item) => item.id), "tidy");
      if (result?.changed) say(result.summary, { action: { label: "Undo", run: () => void undoVersion(result.version) } });
      else if (result) say("Already tidy.");
    } catch (failure) {
      say(failure instanceof Error ? failure.message : "Tidy failed.");
    }
  }, [say, undoVersion, write]);

  const revertTo = useCallback(async (version: number) => {
    const current = docRef.current;
    if (!current) return;
    try {
      const result = await api.revert(id, version, current.version);
      expect(result.version);
      history.current = recordEdit(history.current, result.version);
      say(result.summary);
    } catch (failure) {
      say(failure instanceof Error ? failure.message : "Revert failed.");
    }
  }, [expect, id, say]);

  return {
    doc, error, fresh, notice, live, reload, write, move, undo, redo, undoVersion, tidy, revertTo, say,
    dismissNotice: () => setNotice(null),
  };
}

export type CanvasController = ReturnType<typeof useCanvas>;
