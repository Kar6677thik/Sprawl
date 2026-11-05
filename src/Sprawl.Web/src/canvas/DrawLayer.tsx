import { useRef, useState, type PointerEvent, type WheelEvent } from "react";
import { useReactFlow } from "@xyflow/react";
import { toStroke, type Point, type Stroke } from "./ink";

export const INK_TINTS = [null, "blue", "red", "green", "amber", "violet"] as const;
export type InkTint = (typeof INK_TINTS)[number];

interface Props {
  tint: InkTint;
  width: number;
  onStroke: (stroke: Stroke) => void;
}

/**
 * Draw mode: a transparent layer over the canvas that turns pointer drags into strokes. The live line is drawn in
 * screen space; the finished stroke is converted to canvas coordinates, so it lands where it was drawn at any zoom.
 * The wheel still zooms around the cursor (the layer would otherwise swallow it), as it does on the canvas.
 */
export function DrawLayer({ tint, width, onStroke }: Props) {
  const flow = useReactFlow();
  const [live, setLive] = useState<Point[]>([]);
  const points = useRef<Point[]>([]);
  const drawing = useRef(false);

  const down = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    drawing.current = true;
    points.current = [{ x: event.clientX, y: event.clientY }];
    setLive(points.current);
  };
  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!drawing.current) return;
    points.current = [...points.current, { x: event.clientX, y: event.clientY }];
    setLive(points.current);
  };
  const up = () => {
    if (!drawing.current) return;
    drawing.current = false;
    const stroke = toStroke(points.current.map((p) => flow.screenToFlowPosition(p)), width);
    points.current = [];
    setLive([]);
    if (stroke) onStroke(stroke);
  };

  const wheel = (event: WheelEvent<HTMLDivElement>) => {
    const view = flow.getViewport();
    const rect = event.currentTarget.getBoundingClientRect();
    const zoom = Math.min(2, Math.max(0.05, view.zoom * Math.exp(-event.deltaY * 0.002)));
    const [px, py] = [event.clientX - rect.left, event.clientY - rect.top];
    void flow.setViewport({ x: px - ((px - view.x) * zoom) / view.zoom, y: py - ((py - view.y) * zoom) / view.zoom, zoom });
  };

  const box = useRef<HTMLDivElement>(null);
  const origin = box.current?.getBoundingClientRect();
  const zoom = flow.getViewport().zoom;
  return (
    <div ref={box} className="draw-layer" data-tint={tint ?? undefined} onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={up} onWheel={wheel}>
      {live.length > 1 && origin && (
        <svg className="draw-live" width="100%" height="100%">
          <polyline points={live.map((p) => `${p.x - origin.left},${p.y - origin.top}`).join(" ")} fill="none" stroke="currentColor"
            strokeWidth={width * zoom} strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      )}
    </div>
  );
}
