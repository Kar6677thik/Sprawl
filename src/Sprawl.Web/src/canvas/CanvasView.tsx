import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { applyNodeChanges, Background, ReactFlow, ReactFlowProvider, useReactFlow, useStore, type Node, type NodeChange } from "@xyflow/react";
import { api } from "../api/client";
import type { CanvasDocument, CardKind, LayoutItem } from "../api/types";
import { navigate } from "../router";
import { applyTheme, nextTheme, readTheme } from "../theme/theme";
import { Palette, type PaletteEntry, type PaletteMode } from "../palette/Palette";
import { CardPanel } from "./CardPanel";
import { Composer, type ComposerMode } from "./Composer";
import { edgeTypes } from "./edges";
import { absolute, LINK_EDGE_PREFIX, toEdges, toLinkEdges, toNodes, type SprawlNode } from "./flow";
import { regionAt, relativeTo } from "./geometry";
import { HistoryPanel } from "./HistoryPanel";
import { InsightsPanel } from "./InsightsPanel";
import { ReviewPanel } from "./ReviewPanel";
import { Replay } from "./Replay";
import { LinkContext, type LinkActions } from "./parts";
import { LinkIndex, mayBeExternal } from "./semantics";
import { warmTidy } from "./tidy";
import { nodeTypes, ZoomLevelContext } from "./nodes";
import { Shortcuts } from "./Shortcuts";
import type { CanvasController } from "./useCanvas";
import { LEVEL_ZOOM, levelFor, type ZoomLevel } from "./zoom";

/** Saved palette searches, per canvas and per browser: they are a view preference, not canvas content. */
const savedKey = (canvasId: string) => `sprawl:saved-searches:${canvasId}`;
function readSaved(canvasId: string): string[] {
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(savedKey(canvasId)) ?? "[]");
    return Array.isArray(value) ? value.filter((q): q is string => typeof q === "string").slice(0, 20) : [];
  } catch { return []; }
}
function writeSaved(canvasId: string, queries: string[]) {
  try { window.localStorage.setItem(savedKey(canvasId), JSON.stringify(queries)); } catch { /* storage full or blocked: keep it in memory */ }
}

/** Quick captures land here (the server creates the region on first use); Claude's triage_inbox prompt sorts it. */
const INBOX = "Inbox";
const CAPTURE = { kind: "card" as const, where: `in ${INBOX}`, region: INBOX, capture: true };

interface Props {
  canvas: CanvasController;
  initialFocus?: string | null;
}

export function CanvasView(props: Props) {
  return (
    <ReactFlowProvider>
      <Flow {...props} />
    </ReactFlowProvider>
  );
}

type Panel = { kind: "card"; id: string } | { kind: "history" } | { kind: "insights" } | { kind: "review" } | null;
type Composing = (ComposerMode & { region?: string; at?: { x: number; y: number }; capture?: boolean }) | null;

function Flow({ canvas, initialFocus }: Props) {
  // While replaying history the view draws a past, read-only state instead of the live document.
  const [replaying, setReplaying] = useState(false);
  const [pastDoc, setPastDoc] = useState<CanvasDocument | null>(null);
  const doc = (replaying ? pastDoc : null) ?? canvas.doc!;
  const [level, setLevel] = useState<ZoomLevel>("mid");
  const [nodes, setNodes] = useState<SprawlNode[]>(() => toNodes(doc, canvas.fresh));
  const [panel, setPanel] = useState<Panel>(null);
  const [palette, setPalette] = useState<PaletteMode | null>(null);
  const [paletteQuery, setPaletteQuery] = useState("");
  const [composing, setComposing] = useState<Composing>(null);
  const [connectFrom, setConnectFrom] = useState<string | null>(null);
  const [shortcuts, setShortcuts] = useState(false);
  const [presenting, setPresenting] = useState<number | null>(null);
  const dragging = useRef(false);
  const pointer = useRef<{ x: number; y: number } | null>(null);
  const focusedInitial = useRef(false);
  const flow = useReactFlow();

  // The document is the truth; keep local selection, but never yank a node out from under an active drag.
  useEffect(() => {
    if (dragging.current) return;
    setNodes((previous) => {
      const selected = new Set(previous.filter((node) => node.selected).map((node) => node.id));
      return toNodes(doc, canvas.fresh, level).map((node) => (selected.has(node.id) ? { ...node, selected: true } : node));
    });
  }, [doc, canvas.fresh, level]);

  const linkIndex = useMemo(() => new LinkIndex(doc), [doc]);
  const textLinks = useMemo(() => linkIndex.links(doc), [doc, linkIndex]);
  const edges = useMemo(() => [...toEdges(doc.connections, level), ...toLinkEdges(textLinks, doc.connections, level)], [doc.connections, level, textLinks]);
  const onNodesChange = useCallback((changes: NodeChange<SprawlNode>[]) => setNodes((current) => applyNodeChanges(changes, current)), []);

  /** Centre an item at reading zoom (works from the zoomed-out map, where cards are not nodes). */
  const focus = useCallback((id: string) => {
    const rect = absolute(doc, id);
    if (!rect) return;
    const zoom = id.startsWith("rg_") ? Math.min(1, Math.max(0.35, Math.min(window.innerWidth / (rect.w + 160), window.innerHeight / (rect.h + 200)))) : 1;
    void flow.setCenter(rect.x + rect.w / 2, rect.y + rect.h / 2, { zoom, duration: 350 });
    setNodes((current) => current.map((node) => ({ ...node, selected: node.id === id })));
  }, [doc, flow]);

  const openCard = useCallback((id: string) => { focus(id); setPanel({ kind: "card", id }); }, [focus]);

  /** From a panel: a card opens in the panel, a region is focused. */
  const openOrFocus = useCallback((id: string) => (id.startsWith("cd_") ? openCard(id) : focus(id)), [focus, openCard]);

  const openPalette = useCallback((mode: PaletteMode, query = "") => { setPaletteQuery(query); setPalette(mode); }, []);
  const showTag = useCallback((tag: string) => openPalette("canvas", `#${tag}`), [openPalette]);

  /** `[[links]]` open their card; a link naming no card yet offers to create it beside the card that links to it. */
  const linkActions = useMemo<LinkActions>(() => ({
    open: (target) => {
      const found = linkIndex.resolve(target);
      if (found) { openCard(found.id); return; }
      if (mayBeExternal(target)) {
        void api.resolveLink(doc.meta.id, target).then(
          (hit) => navigate(`/c/${hit.canvasId}?focus=${hit.cardId}`),
          () => canvas.say(`No card matches [[${target}]] on any canvas.`));
        return;
      }
      const from = panel?.kind === "card" ? doc.cards.find((c) => c.id === panel.id) : undefined;
      canvas.say(`No card called "${target}" yet.`, { action: { label: "Create it", run: () => void canvas.write([{
        type: "addCard", ref: "new", kind: "note", title: target, ...(from?.regionId ? { region: from.regionId } : {}),
      }], []).then((result) => { const id = result?.refs["new"]; if (id) window.setTimeout(() => openCard(id), 300); }) } });
    },
    tag: showTag,
    exists: (target) => linkIndex.resolve(target) !== undefined || mayBeExternal(target),
  }), [canvas, doc.cards, doc.meta.id, linkIndex, openCard, panel, showTag]);

  useEffect(() => {
    if (focusedInitial.current || !initialFocus) return;
    focusedInitial.current = true;
    window.setTimeout(() => (initialFocus.startsWith("cd_") ? openCard(initialFocus) : focus(initialFocus)), 50);
  }, [focus, initialFocus, openCard]);

  useEffect(() => {
    const idle = window.setTimeout(warmTidy, 1500);
    return () => window.clearTimeout(idle);
  }, []);

  const onNodeDragStart = useCallback(() => { dragging.current = true; }, []);
  /** Dropping a card inside another region moves it there; dropping it on open canvas makes it a loose card. */
  const onNodeDragStop = useCallback((_event: unknown, _node: Node, dragged: Node[]) => {
    dragging.current = false;
    const draggedIds = new Set(dragged.map((n) => n.id));
    const items: LayoutItem[] = dragged.map((node) => {
      const item: LayoutItem = { id: node.id, x: Math.round(node.position.x), y: Math.round(node.position.y) };
      const card = doc.cards.find((c) => c.id === node.id);
      if (!card || (card.regionId && draggedIds.has(card.regionId))) return item;
      const internal = flow.getInternalNode(node.id);
      if (!internal) return item;
      const topLeft = internal.internals.positionAbsolute;
      const target = regionAt(doc, { x: topLeft.x + card.w / 2, y: topLeft.y + card.h / 2 });
      if (target === card.regionId) return item;
      const position = relativeTo(doc, target, topLeft);
      return { id: node.id, ...position, container: target ?? "canvas" };
    });
    void canvas.move(items);
  }, [canvas, doc, flow]);

  const onNodeClick = useCallback((_event: unknown, node: Node) => {
    if (!connectFrom || replaying) return;
    if (node.id !== connectFrom) void canvas.write([{ type: "connect", from: connectFrom, to: node.id }], [connectFrom, node.id]);
    setConnectFrom(null);
  }, [canvas, connectFrom, replaying]);

  const onNodeDoubleClick = useCallback((_event: unknown, node: Node) => {
    if (node.id.startsWith("cd_") && !replaying) setPanel({ kind: "card", id: node.id });
    else focus(node.id);
  }, [focus, replaying]);

  const selection = useCallback(() => ({
    nodes: flow.getNodes().filter((n) => n.selected).map((n) => n.id),
    edges: flow.getEdges().filter((e) => e.selected).map((e) => e.id),
  }), [flow]);

  /** Where a new card goes: into the selected region (or the selected card's region), else under the cursor. */
  const startCard = useCallback(() => {
    const selected = selection().nodes[0];
    const region = selected?.startsWith("rg_") ? selected : doc.cards.find((c) => c.id === selected)?.regionId ?? undefined;
    if (region) {
      setComposing({ kind: "card", where: `in ${doc.regions.find((r) => r.id === region)?.title ?? "region"}`, region });
      return;
    }
    const screen = pointer.current ?? { x: window.innerWidth / 2, y: window.innerHeight / 2 };
    const point = flow.screenToFlowPosition(screen);
    const target = regionAt(doc, point);
    setComposing({
      kind: "card",
      where: target ? `in ${doc.regions.find((r) => r.id === target)?.title}` : "here",
      ...(target ? { region: target } : {}),
      at: relativeTo(doc, target, point),
    });
  }, [doc, flow, selection]);

  const createCard = useCallback(async (title: string, kind: CardKind) => {
    const target = composing;
    setComposing(null);
    if (!target || target.kind !== "card") return;
    // A selected region gets an auto-placed card; a card dropped "here" on the canvas or in a region keeps that spot.
    const result = await canvas.write([{
      type: "addCard", ref: "new", kind, title,
      ...(target.region ? { region: target.region } : {}),
      ...(target.at ? { at: target.at } : {}),
    }], []);
    const id = result?.refs["new"];
    // Capture keeps the composer open for the next thought; a normal quick-add opens the new card.
    if (target.capture) { if (id) canvas.say(`Captured to ${INBOX}.`, { focus: id }); setComposing({ ...target }); }
    else if (id) setPanel({ kind: "card", id });
  }, [canvas, composing]);

  const createRegion = useCallback(async (title: string) => {
    setComposing(null);
    const result = await canvas.write([{ type: "addRegion", ref: "new", title }], []);
    const id = result?.refs["new"];
    if (id) window.setTimeout(() => focus(id), 400);
  }, [canvas, focus]);

  const deleteSelection = useCallback(() => {
    const { nodes: ids, edges: allEdges } = selection();
    const edgeIds = allEdges.filter((id) => !id.startsWith(LINK_EDGE_PREFIX));
    const cards = ids.filter((id) => id.startsWith("cd_"));
    const regions = ids.filter((id) => id.startsWith("rg_"));
    if (cards.length === 0 && edgeIds.length === 0 && regions.length === 0) return;
    // Regions are deleted only when empty (or emptied by this same delete); otherwise the server refuses.
    const emptyRegions = regions.filter((id) => doc.cards.every((c) => c.regionId !== id || cards.includes(c.id)) && !doc.regions.some((r) => r.parentId === id));
    if (regions.length > emptyRegions.length) canvas.say("Regions with cards are kept; move or delete their cards first.");
    if (cards.length === 0 && edgeIds.length === 0 && emptyRegions.length === 0) return;
    void canvas.write([
      ...(cards.length > 0 ? [{ type: "deleteCards" as const, ids: cards }] : []),
      ...edgeIds.filter((id) => !id.startsWith(LINK_EDGE_PREFIX)).map((id) => ({ type: "disconnect" as const, id })),
      ...emptyRegions.map((id) => ({ type: "deleteRegion" as const, id })),
    ], [...cards, ...edgeIds, ...emptyRegions]).then((result) => {
      if (result?.changed) canvas.say(result.summary, { action: { label: "Undo", run: () => void canvas.undoVersion(result.version) } });
    });
    if (panel?.kind === "card" && cards.includes(panel.id)) setPanel(null);
  }, [canvas, doc.cards, doc.regions, panel, selection]);

  /** Present: top-level regions in reading order, one per step, framed to fill the screen. */
  const steps = useMemo(() => doc.regions.filter((r) => r.parentId === null).sort((a, b) => a.y - b.y || a.x - b.x || a.id.localeCompare(b.id)), [doc.regions]);
  const present = useCallback((index: number | null) => {
    if (index === null || steps.length === 0) { setPresenting(null); return; }
    const step = Math.max(0, Math.min(index, steps.length - 1));
    setPresenting(step);
    setPanel(null);
    const region = steps[step]!;
    void flow.fitBounds({ x: region.x, y: region.y, width: region.w, height: region.h }, { padding: 0.08, duration: 500 });
  }, [flow, steps]);

  /** The current view as a PNG at twice screen resolution: frame what you want (the whole map, or one region), then export. */
  const exportPng = useCallback(async () => {
    const viewport = document.querySelector<HTMLElement>(".flow .react-flow__viewport");
    const pane = document.querySelector<HTMLElement>(".flow .react-flow");
    if (!viewport || !pane) return;
    try {
      const { toPng } = await import("html-to-image");
      const { x, y, zoom } = flow.getViewport();
      const url = await toPng(viewport, {
        width: pane.clientWidth,
        height: pane.clientHeight,
        pixelRatio: 2,
        backgroundColor: getComputedStyle(pane).getPropertyValue("--canvas").trim() || getComputedStyle(document.body).backgroundColor,
        style: { width: `${pane.clientWidth}px`, height: `${pane.clientHeight}px`, transform: `translate(${x}px, ${y}px) scale(${zoom})` },
      });
      const link = document.createElement("a");
      link.href = url;
      link.download = `${doc.meta.title.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, "-").replace(/^-|-$/g, "") || "canvas"}.png`;
      link.click();
    } catch {
      canvas.say("The image could not be made.");
    }
  }, [canvas, doc.meta.title, flow]);

  const download = useCallback((format: "markdown" | "json-canvas") => {
    const link = document.createElement("a");
    link.href = api.exportUrl(doc.meta.id, format);
    link.download = "";
    link.click();
  }, [doc.meta.id]);

  /** Collections are one level of folders on the home page; typing a name in the palette offers to file this canvas there. */
  const [saved, setSaved] = useState<string[]>(() => readSaved(doc.meta.id));
  const saveSearches = useCallback((next: string[]) => { setSaved(next); writeSaved(doc.meta.id, next); }, [doc.meta.id]);

  const dynamicEntries = useCallback((query: string, mode: PaletteMode): PaletteEntry[] => {
    const entries: PaletteEntry[] = [];
    // Any search (e.g. "#risk question") can be saved; saved ones are listed under Ctrl+K and rerun with one keystroke.
    if (query && !/^(collection:|move to )/i.test(query)) {
      if (saved.includes(query))
        entries.push({ id: "search-forget", group: "Saved searches", label: `Forget saved search “${query}”`, run: () => saveSearches(saved.filter((q) => q !== query)) });
      else if (mode === "canvas" || query.startsWith("#"))
        entries.push({ id: "search-save", group: "Saved searches", label: `Save search “${query}”`, run: () => { saveSearches([...saved, query].slice(-20)); canvas.say(`Saved “${query}”: find it under Ctrl+K.`); } });
    }
    if (mode !== "all") return entries;
    const prefixed = /^(collection:|move to )/i.test(query);
    const name = query.replace(/^(collection:|move to )\s*/i, "").trim();
    if (prefixed && name && name.length <= 80 && name.toLowerCase() !== (doc.meta.collection ?? "").toLowerCase())
      entries.push({ id: "collection-set", group: "Collection", label: `Move this canvas to “${name}”`,
        run: () => void canvas.write([{ type: "updateCanvas", collection: name }], []) });
    if (doc.meta.collection && /^(collection|remove from)/i.test(query))
      entries.push({ id: "collection-clear", group: "Collection", label: `Take this canvas out of “${doc.meta.collection}”`,
        run: () => void canvas.write([{ type: "updateCanvas", clearCollection: true }], []) });
    return entries;
  }, [canvas, doc.meta.collection, saveSearches, saved]);

  const tidySelection = useCallback(() => {
    const selected = selection().nodes[0];
    const region = selected?.startsWith("rg_") ? selected : doc.cards.find((c) => c.id === selected)?.regionId ?? null;
    void canvas.tidy(region);
  }, [canvas, doc.cards, selection]);

  const actions = useMemo<PaletteEntry[]>(() => [
    ...saved.map((query) => ({ id: `saved:${query}`, group: "Saved searches", label: query, hint: "saved search", run: () => window.setTimeout(() => openPalette("canvas", query)) })),
    { id: "new-card", group: "Actions", label: "New card", keys: "N", run: startCard },
    { id: "new-region", group: "Actions", label: "New region", keys: "⇧N", run: () => setComposing({ kind: "region" }) },
    { id: "search", group: "Actions", label: "Search this canvas", keys: "/", run: () => window.setTimeout(() => openPalette("canvas")) },
    { id: "search-all", group: "Actions", label: "Search all canvases", keys: "Ctrl ⇧F", run: () => window.setTimeout(() => openPalette("global")) },
    { id: "tags", group: "Actions", label: "Browse by tag", hint: "#tag", run: () => window.setTimeout(() => openPalette("canvas", "#")) },
    { id: "insights", group: "Actions", label: "Insights", hint: "gaps, orphans, missing cards", keys: "I", run: () => setPanel({ kind: "insights" }) },
    { id: "tidy", group: "Actions", label: "Tidy canvas", run: () => void canvas.tidy(null) },
    { id: "tidy-region", group: "Actions", label: "Tidy selected region", keys: "T", run: tidySelection },
    { id: "fit", group: "Actions", label: "Fit canvas", keys: "⇧F", run: () => void flow.fitView({ duration: 300, padding: 0.1 }) },
    { id: "zoom-map", group: "Actions", label: "Zoom: map", keys: "1", run: () => void flow.zoomTo(LEVEL_ZOOM.far, { duration: 250 }) },
    { id: "zoom-titles", group: "Actions", label: "Zoom: titles", keys: "2", run: () => void flow.zoomTo(LEVEL_ZOOM.mid, { duration: 250 }) },
    { id: "zoom-bodies", group: "Actions", label: "Zoom: bodies", keys: "3", run: () => void flow.zoomTo(LEVEL_ZOOM.near, { duration: 250 }) },
    { id: "undo", group: "Actions", label: "Undo", keys: "Ctrl Z", run: () => void canvas.undo() },
    { id: "redo", group: "Actions", label: "Redo", keys: "Ctrl ⇧Z", run: () => void canvas.redo() },
    { id: "history", group: "Actions", label: "History", keys: "H", run: () => setPanel({ kind: "history" }) },
    { id: "capture", group: "Actions", label: "Capture to Inbox", hint: "quick thoughts, sort later", keys: "⇧I", run: () => setComposing(CAPTURE) },
    { id: "review", group: "Actions", label: "Review open questions", keys: "R", run: () => setPanel({ kind: "review" }) },
    { id: "replay", group: "Actions", label: "Replay history", hint: "watch the canvas grow", keys: "⇧H", run: () => { setPanel(null); setReplaying(true); } },
    { id: "present", group: "Actions", label: "Present", hint: "regions one by one", keys: "P", run: () => present(0) },
    { id: "export-md", group: "Actions", label: "Export as Markdown", run: () => download("markdown") },
    { id: "export-canvas", group: "Actions", label: "Export as JSON Canvas", hint: "Obsidian .canvas", run: () => download("json-canvas") },
    { id: "export-png", group: "Actions", label: "Export view as PNG", hint: "what is on screen, at 2×", run: () => void exportPng() },
    { id: "copy-md", group: "Actions", label: "Copy canvas as Markdown", run: () => void api.exportMarkdown(doc.meta.id)
      .then((text) => navigator.clipboard.writeText(text)).then(() => canvas.say("Copied the canvas as Markdown."), () => canvas.say("Could not copy the canvas.")) },
    { id: "theme", group: "Actions", label: "Switch theme", run: () => applyTheme(nextTheme(readTheme())) },
    { id: "shortcuts", group: "Actions", label: "Keyboard shortcuts", keys: "?", run: () => setShortcuts(true) },
    { id: "home", group: "Actions", label: "All canvases", run: () => navigate("/") },
    { id: "backup", group: "Actions", label: "Back up now", hint: "verified copy of every canvas", run: () => void api.backupNow()
      .then((r) => canvas.say(`Backed up: ${r.file}`), (e: unknown) => canvas.say(e instanceof Error ? e.message : "Backup failed.")) },
  ], [canvas, doc.meta.id, download, exportPng, flow, openPalette, present, saved, startCard, tidySelection]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const mod = event.ctrlKey || event.metaKey;
      const key = event.key;
      if (replaying) return; // Replay owns the keyboard; nothing may edit a past state.
      if (presenting !== null && !mod) {
        const moves: Record<string, number> = { ArrowRight: 1, ArrowDown: 1, PageDown: 1, " ": 1, ArrowLeft: -1, ArrowUp: -1, PageUp: -1 };
        if (key === "Escape") { event.preventDefault(); present(null); return; }
        if (key === "Home") { event.preventDefault(); present(0); return; }
        if (key === "End") { event.preventDefault(); present(steps.length - 1); return; }
        if (moves[key] !== undefined) { event.preventDefault(); present(presenting + moves[key]!); return; }
      }
      if (mod && key.toLowerCase() === "k") { event.preventDefault(); setPaletteQuery(""); setPalette((open) => (open ? null : "all")); return; }
      if (mod && event.shiftKey && key.toLowerCase() === "f") { event.preventDefault(); openPalette("global"); return; }
      // Keys typed into a field are text, not shortcuts. (The target is not always an element, e.g. the window.)
      const typing = event.target instanceof Element && event.target.closest("input, textarea, select, [contenteditable=true]") !== null;
      if (typing || palette || shortcuts || composing) return;
      if (mod && key.toLowerCase() === "z") { event.preventDefault(); void (event.shiftKey ? canvas.redo() : canvas.undo()); return; }
      if (mod && key.toLowerCase() === "y") { event.preventDefault(); void canvas.redo(); return; }
      if (mod || event.altKey) return;
      if (key === "Escape") { setConnectFrom(null); setPanel(null); return; }
      const zoomTo: Record<string, ZoomLevel> = { "1": "far", "2": "mid", "3": "near" };
      if (zoomTo[key]) { void flow.zoomTo(LEVEL_ZOOM[zoomTo[key]!], { duration: 250 }); return; }
      const selected = selection().nodes;
      switch (key) {
        case "/": event.preventDefault(); openPalette("canvas"); return;
        case "#": event.preventDefault(); openPalette("canvas", "#"); return;
        case "p": present(0); return;
        case "I": event.preventDefault(); setComposing(CAPTURE); return;
        case "r": setPanel((p) => (p?.kind === "review" ? null : { kind: "review" })); return;
        case "i": setPanel((p) => (p?.kind === "insights" ? null : { kind: "insights" })); return;
        case "?": setShortcuts(true); return;
        case "N": event.preventDefault(); setComposing({ kind: "region" }); return;
        case "n": event.preventDefault(); startCard(); return;
        case "H": setPanel(null); setReplaying(true); return;
        case "h": setPanel((p) => (p?.kind === "history" ? null : { kind: "history" })); return;
        case "t": tidySelection(); return;
        case "F": void flow.fitView({ duration: 300, padding: 0.1 }); return;
        case "f": if (selected[0]) focus(selected[0]); return;
        case "c": if (selected[0]) setConnectFrom(selected[0]); return;
        case "Enter": if (selected[0]?.startsWith("cd_")) { event.preventDefault(); setPanel({ kind: "card", id: selected[0] }); } return;
        case "Delete":
        case "Backspace": event.preventDefault(); deleteSelection(); return;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [canvas, composing, deleteSelection, flow, focus, openPalette, palette, present, presenting, replaying, selection, shortcuts, startCard, steps.length, tidySelection]);

  const connectTitle = connectFrom ? doc.cards.find((c) => c.id === connectFrom)?.title ?? doc.regions.find((r) => r.id === connectFrom)?.title : null;

  return (
    <ZoomLevelContext.Provider value={level}>
      <LinkContext.Provider value={linkActions}>
      <div className={`flow level-${level}${connectFrom ? " is-connecting" : ""}${presenting !== null ? " is-presenting" : ""}${replaying ? " is-replaying" : ""}`} onPointerMove={(e) => { pointer.current = { x: e.clientX, y: e.clientY }; }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          onNodesChange={onNodesChange}
          nodesDraggable={!replaying}
          onNodeDragStart={onNodeDragStart}
          onNodeDragStop={onNodeDragStop}
          onNodeClick={onNodeClick}
          onNodeDoubleClick={onNodeDoubleClick}
          onPaneClick={() => setConnectFrom(null)}
          fitView
          fitViewOptions={{ padding: 0.1, maxZoom: 1 }}
          minZoom={0.05}
          maxZoom={2}
          onlyRenderVisibleElements
          nodesConnectable={false}
          elevateNodesOnSelect={false}
          deleteKeyCode={null}
          zoomOnDoubleClick={false}
          multiSelectionKeyCode="Shift"
          zIndexMode="manual"
        >
          <Background gap={24} size={1} />
          <ZoomTracker onLevel={setLevel} />
        </ReactFlow>
        {replaying && canvas.doc && (
          <Replay canvasId={canvas.doc.meta.id} latest={canvas.doc.version} onState={setPastDoc}
            onRestore={(v) => { setReplaying(false); void canvas.revertTo(v); }} onExit={() => setReplaying(false)} />
        )}
        {presenting !== null && steps[presenting] && (
          <div className="presenter" role="status" aria-live="polite">
            <button type="button" className="icon-button" aria-label="Previous region" disabled={presenting === 0} onClick={() => present(presenting - 1)}>‹</button>
            <span className="presenter-title">{steps[presenting]!.title}</span>
            <span className="presenter-count">{presenting + 1} / {steps.length}</span>
            <button type="button" className="icon-button" aria-label="Next region" disabled={presenting === steps.length - 1} onClick={() => present(presenting + 1)}>›</button>
            <button type="button" className="quiet small" onClick={() => present(null)}>Done <kbd>Esc</kbd></button>
          </div>
        )}
        {connectFrom && (
          <div className="mode-banner" role="status">Connect <strong>{connectTitle}</strong> to… click a card or region · <kbd>Esc</kbd> cancels</div>
        )}
        {composing && (
          <Composer mode={composing} onCancel={() => setComposing(null)}
            onSubmit={(title, kind) => void (composing.kind === "card" ? createCard(title, kind) : createRegion(title))} />
        )}
        {canvas.notice && (
          <div className="toast" role="status" key={canvas.notice.id}>
            <span>{canvas.notice.text}</span>
            {canvas.notice.focus && <button type="button" onClick={() => { focus(canvas.notice!.focus!); canvas.dismissNotice(); }}>View</button>}
            {canvas.notice.action && <button type="button" onClick={() => { canvas.notice!.action!.run(); canvas.dismissNotice(); }}>{canvas.notice.action.label}</button>}
            <button type="button" className="quiet" aria-label="Dismiss" onClick={canvas.dismissNotice}>×</button>
          </div>
        )}
        {panel?.kind === "card" && <CardPanel doc={doc} cardId={panel.id} write={canvas.write} onClose={() => setPanel(null)} onFocus={openOrFocus} onTag={showTag} />}
        {panel?.kind === "review" && (
          <ReviewPanel canvasId={doc.meta.id} onOpen={openCard} onClose={() => setPanel(null)}
            onAsk={() => void navigator.clipboard.writeText(`Use the Sprawl answer_questions prompt for canvas ${doc.meta.id}: research the open questions due for review and add what you find.`)
              .then(() => canvas.say("Prompt copied: paste it into Claude."), () => canvas.say("Could not copy to the clipboard."))} />
        )}
        {panel?.kind === "insights" && (
          <InsightsPanel canvasId={doc.meta.id} version={doc.version} write={canvas.write} onFocus={focus} onTag={showTag} say={(text) => canvas.say(text)} onClose={() => setPanel(null)} />
        )}
        {panel?.kind === "history" && (
          <HistoryPanel canvasId={doc.meta.id} version={doc.version} onUndo={(v) => void canvas.undoVersion(v)} onRevert={(v) => void canvas.revertTo(v)} onClose={() => setPanel(null)} />
        )}
      </div>
      {palette && (
        <Palette mode={palette} doc={doc} initialQuery={paletteQuery} dynamic={dynamicEntries} actions={actions} onClose={() => setPalette(null)} onFocus={focus} onOpenCard={openCard}
          onOpenCanvas={(id, card) => navigate(`/c/${id}${card ? `?focus=${card}` : ""}`)} />
      )}
      {shortcuts && <Shortcuts onClose={() => setShortcuts(false)} />}
      </LinkContext.Provider>
    </ZoomLevelContext.Provider>
  );
}

/** Publishes the zoom as a CSS variable (for zoom-compensated region titles) and the semantic level as state. */
function ZoomTracker({ onLevel }: { onLevel: (level: ZoomLevel) => void }) {
  const zoom = useStore((state) => state.transform[2]);
  const current = useRef<ZoomLevel | undefined>(undefined);
  useEffect(() => {
    document.documentElement.style.setProperty("--sprawl-zoom", zoom.toFixed(3));
    const next = levelFor(zoom, current.current);
    if (next !== current.current) {
      current.current = next;
      onLevel(next);
    }
  }, [zoom, onLevel]);
  return null;
}
