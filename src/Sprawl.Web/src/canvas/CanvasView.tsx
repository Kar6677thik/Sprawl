import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { applyNodeChanges, Background, ReactFlow, ReactFlowProvider, useReactFlow, useStore, type Node, type NodeChange } from "@xyflow/react";
import type { CanvasDocument, LayoutItem } from "../api/types";
import { toEdges, toNodes, type SprawlNode } from "./flow";
import { nodeTypes, ZoomLevelContext } from "./nodes";
import type { CanvasNotice } from "./useCanvas";
import { LEVEL_ZOOM, levelFor, type ZoomLevel } from "./zoom";

interface Props {
  doc: CanvasDocument;
  fresh: ReadonlySet<string>;
  notice: CanvasNotice | null;
  onMove: (items: LayoutItem[]) => void;
  onDismissNotice: () => void;
}

export function CanvasView(props: Props) {
  return (
    <ReactFlowProvider>
      <Flow {...props} />
    </ReactFlowProvider>
  );
}

function Flow({ doc, fresh, notice, onMove, onDismissNotice }: Props) {
  const [level, setLevel] = useState<ZoomLevel>("mid");
  const [nodes, setNodes] = useState<SprawlNode[]>(() => toNodes(doc, fresh));
  const dragging = useRef(false);
  const flow = useReactFlow();

  // The document is the truth; keep local selection, but never yank a node out from under an active drag.
  useEffect(() => {
    if (dragging.current) return;
    setNodes((previous) => {
      const selected = new Set(previous.filter((node) => node.selected).map((node) => node.id));
      return toNodes(doc, fresh, level).map((node) => (selected.has(node.id) ? { ...node, selected: true } : node));
    });
  }, [doc, fresh, level]);

  const edges = useMemo(() => toEdges(doc.connections, level), [doc.connections, level]);
  const onNodesChange = useCallback((changes: NodeChange<SprawlNode>[]) => setNodes((current) => applyNodeChanges(changes, current)), []);
  const onNodeDragStart = useCallback(() => { dragging.current = true; }, []);
  const onNodeDragStop = useCallback((_event: unknown, _node: Node, dragged: Node[]) => {
    dragging.current = false;
    onMove(dragged.map((node) => ({ id: node.id, x: Math.round(node.position.x), y: Math.round(node.position.y) })));
  }, [onMove]);

  const focus = useCallback((id: string) => void flow.fitView({ nodes: [{ id }], duration: 300, maxZoom: 1, padding: 0.3 }), [flow]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // Keys typed into a field are text, not shortcuts. (The target is not always an element, e.g. the window.)
      const typing = event.target instanceof Element && event.target.closest("input, textarea, [contenteditable=true]") !== null;
      if (event.ctrlKey || event.metaKey || event.altKey || typing) return;
      const zoomTo: Record<string, ZoomLevel> = { "1": "far", "2": "mid", "3": "near" };
      if (zoomTo[event.key]) { void flow.zoomTo(LEVEL_ZOOM[zoomTo[event.key]!], { duration: 250 }); return; }
      if (event.key === "F" && event.shiftKey) { void flow.fitView({ duration: 300, padding: 0.1 }); return; }
      if (event.key === "f") {
        const selected = flow.getNodes().find((node) => node.selected);
        if (selected) focus(selected.id);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [flow, focus]);

  return (
    <ZoomLevelContext.Provider value={level}>
      <div className={`flow level-${level}`}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          onNodesChange={onNodesChange}
          onNodeDragStart={onNodeDragStart}
          onNodeDragStop={onNodeDragStop}
          fitView
          fitViewOptions={{ padding: 0.1, maxZoom: 1 }}
          minZoom={0.05}
          maxZoom={2}
          onlyRenderVisibleElements
          nodesConnectable={false}
          elevateNodesOnSelect={false}
          deleteKeyCode={null}
          zoomOnDoubleClick={false}
        >
          <Background gap={24} size={1} />
          <ZoomTracker onLevel={setLevel} />
        </ReactFlow>
        {notice && (
          <div className="toast" role="status" key={notice.id}>
            <span>{notice.text}</span>
            {notice.focus && <button type="button" onClick={() => { focus(notice.focus!); onDismissNotice(); }}>View</button>}
            <button type="button" className="quiet" aria-label="Dismiss" onClick={onDismissNotice}>×</button>
          </div>
        )}
      </div>
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
