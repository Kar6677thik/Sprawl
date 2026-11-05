import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { applyNodeChanges, Background, ReactFlow, ReactFlowProvider, useReactFlow, useStore, type Node, type NodeChange } from "@xyflow/react";
import { api } from "../api/client";
import type { CardKind, LayoutItem } from "../api/types";
import { navigate } from "../router";
import { applyTheme, nextTheme, readTheme } from "../theme/theme";
import { Palette, type PaletteEntry, type PaletteMode } from "../palette/Palette";
import { CardPanel } from "./CardPanel";
import { Composer, type ComposerMode } from "./Composer";
import { edgeTypes } from "./edges";
import { absolute, toEdges, toNodes, type SprawlNode } from "./flow";
import { regionAt, relativeTo } from "./geometry";
import { HistoryPanel } from "./HistoryPanel";
import { warmTidy } from "./tidy";
import { nodeTypes, ZoomLevelContext } from "./nodes";
import { Shortcuts } from "./Shortcuts";
import type { CanvasController } from "./useCanvas";
import { LEVEL_ZOOM, levelFor, type ZoomLevel } from "./zoom";

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

type Panel = { kind: "card"; id: string } | { kind: "history" } | null;
type Composing = (ComposerMode & { region?: string; at?: { x: number; y: number } }) | null;

function Flow({ canvas, initialFocus }: Props) {
  const doc = canvas.doc!;
  const [level, setLevel] = useState<ZoomLevel>("mid");
  const [nodes, setNodes] = useState<SprawlNode[]>(() => toNodes(doc, canvas.fresh));
  const [panel, setPanel] = useState<Panel>(null);
  const [palette, setPalette] = useState<PaletteMode | null>(null);
  const [composing, setComposing] = useState<Composing>(null);
  const [connectFrom, setConnectFrom] = useState<string | null>(null);
  const [shortcuts, setShortcuts] = useState(false);
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

  const edges = useMemo(() => toEdges(doc.connections, level), [doc.connections, level]);
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
    if (!connectFrom) return;
    if (node.id !== connectFrom) void canvas.write([{ type: "connect", from: connectFrom, to: node.id }], [connectFrom, node.id]);
    setConnectFrom(null);
  }, [canvas, connectFrom]);

  const onNodeDoubleClick = useCallback((_event: unknown, node: Node) => {
    if (node.id.startsWith("cd_")) setPanel({ kind: "card", id: node.id });
    else focus(node.id);
  }, [focus]);

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
    if (id) setPanel({ kind: "card", id });
  }, [canvas, composing]);

  const createRegion = useCallback(async (title: string) => {
    setComposing(null);
    const result = await canvas.write([{ type: "addRegion", ref: "new", title }], []);
    const id = result?.refs["new"];
    if (id) window.setTimeout(() => focus(id), 400);
  }, [canvas, focus]);

  const deleteSelection = useCallback(() => {
    const { nodes: ids, edges: edgeIds } = selection();
    const cards = ids.filter((id) => id.startsWith("cd_"));
    const regions = ids.filter((id) => id.startsWith("rg_"));
    if (cards.length === 0 && edgeIds.length === 0 && regions.length === 0) return;
    // Regions are deleted only when empty (or emptied by this same delete); otherwise the server refuses.
    const emptyRegions = regions.filter((id) => doc.cards.every((c) => c.regionId !== id || cards.includes(c.id)) && !doc.regions.some((r) => r.parentId === id));
    if (regions.length > emptyRegions.length) canvas.say("Regions with cards are kept; move or delete their cards first.");
    if (cards.length === 0 && edgeIds.length === 0 && emptyRegions.length === 0) return;
    void canvas.write([
      ...(cards.length > 0 ? [{ type: "deleteCards" as const, ids: cards }] : []),
      ...edgeIds.map((id) => ({ type: "disconnect" as const, id })),
      ...emptyRegions.map((id) => ({ type: "deleteRegion" as const, id })),
    ], [...cards, ...edgeIds, ...emptyRegions]).then((result) => {
      if (result?.changed) canvas.say(result.summary, { action: { label: "Undo", run: () => void canvas.undoVersion(result.version) } });
    });
    if (panel?.kind === "card" && cards.includes(panel.id)) setPanel(null);
  }, [canvas, doc.cards, doc.regions, panel, selection]);

  const tidySelection = useCallback(() => {
    const selected = selection().nodes[0];
    const region = selected?.startsWith("rg_") ? selected : doc.cards.find((c) => c.id === selected)?.regionId ?? null;
    void canvas.tidy(region);
  }, [canvas, doc.cards, selection]);

  const actions = useMemo<PaletteEntry[]>(() => [
    { id: "new-card", group: "Actions", label: "New card", keys: "N", run: startCard },
    { id: "new-region", group: "Actions", label: "New region", keys: "⇧N", run: () => setComposing({ kind: "region" }) },
    { id: "search", group: "Actions", label: "Search this canvas", keys: "/", run: () => window.setTimeout(() => setPalette("canvas")) },
    { id: "search-all", group: "Actions", label: "Search all canvases", keys: "Ctrl ⇧F", run: () => window.setTimeout(() => setPalette("global")) },
    { id: "tidy", group: "Actions", label: "Tidy canvas", run: () => void canvas.tidy(null) },
    { id: "tidy-region", group: "Actions", label: "Tidy selected region", keys: "T", run: tidySelection },
    { id: "fit", group: "Actions", label: "Fit canvas", keys: "⇧F", run: () => void flow.fitView({ duration: 300, padding: 0.1 }) },
    { id: "zoom-map", group: "Actions", label: "Zoom: map", keys: "1", run: () => void flow.zoomTo(LEVEL_ZOOM.far, { duration: 250 }) },
    { id: "zoom-titles", group: "Actions", label: "Zoom: titles", keys: "2", run: () => void flow.zoomTo(LEVEL_ZOOM.mid, { duration: 250 }) },
    { id: "zoom-bodies", group: "Actions", label: "Zoom: bodies", keys: "3", run: () => void flow.zoomTo(LEVEL_ZOOM.near, { duration: 250 }) },
    { id: "undo", group: "Actions", label: "Undo", keys: "Ctrl Z", run: () => void canvas.undo() },
    { id: "redo", group: "Actions", label: "Redo", keys: "Ctrl ⇧Z", run: () => void canvas.redo() },
    { id: "history", group: "Actions", label: "History", keys: "H", run: () => setPanel({ kind: "history" }) },
    { id: "theme", group: "Actions", label: "Switch theme", run: () => applyTheme(nextTheme(readTheme())) },
    { id: "shortcuts", group: "Actions", label: "Keyboard shortcuts", keys: "?", run: () => setShortcuts(true) },
    { id: "home", group: "Actions", label: "All canvases", run: () => navigate("/") },
    { id: "backup", group: "Actions", label: "Back up now", hint: "verified copy of every canvas", run: () => void api.backupNow()
      .then((r) => canvas.say(`Backed up: ${r.file}`), (e: unknown) => canvas.say(e instanceof Error ? e.message : "Backup failed.")) },
  ], [canvas, flow, startCard, tidySelection]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const mod = event.ctrlKey || event.metaKey;
      const key = event.key;
      if (mod && key.toLowerCase() === "k") { event.preventDefault(); setPalette((open) => (open ? null : "all")); return; }
      if (mod && event.shiftKey && key.toLowerCase() === "f") { event.preventDefault(); setPalette("global"); return; }
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
        case "/": event.preventDefault(); setPalette("canvas"); return;
        case "?": setShortcuts(true); return;
        case "N": event.preventDefault(); setComposing({ kind: "region" }); return;
        case "n": event.preventDefault(); startCard(); return;
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
  }, [canvas, composing, deleteSelection, flow, focus, palette, selection, shortcuts, startCard, tidySelection]);

  const connectTitle = connectFrom ? doc.cards.find((c) => c.id === connectFrom)?.title ?? doc.regions.find((r) => r.id === connectFrom)?.title : null;

  return (
    <ZoomLevelContext.Provider value={level}>
      <div className={`flow level-${level}${connectFrom ? " is-connecting" : ""}`} onPointerMove={(e) => { pointer.current = { x: e.clientX, y: e.clientY }; }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          onNodesChange={onNodesChange}
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
        {panel?.kind === "card" && <CardPanel doc={doc} cardId={panel.id} write={canvas.write} onClose={() => setPanel(null)} onFocus={focus} />}
        {panel?.kind === "history" && (
          <HistoryPanel canvasId={doc.meta.id} version={doc.version} onUndo={(v) => void canvas.undoVersion(v)} onRevert={(v) => void canvas.revertTo(v)} onClose={() => setPanel(null)} />
        )}
      </div>
      {palette && (
        <Palette mode={palette} doc={doc} actions={actions} onClose={() => setPalette(null)} onFocus={focus} onOpenCard={openCard}
          onOpenCanvas={(id, card) => navigate(`/c/${id}${card ? `?focus=${card}` : ""}`)} />
      )}
      {shortcuts && <Shortcuts onClose={() => setShortcuts(false)} />}
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
