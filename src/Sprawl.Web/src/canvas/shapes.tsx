import type { ReactNode } from "react";
import type { Shape } from "../api/types";

export const SHAPES: { shape: Shape; label: string }[] = [
  { shape: "rectangle", label: "Process" },
  { shape: "rounded", label: "Service" },
  { shape: "terminal", label: "Start / end" },
  { shape: "diamond", label: "Decision" },
  { shape: "circle", label: "Event" },
  { shape: "ellipse", label: "Ellipse" },
  { shape: "cylinder", label: "Database" },
  { shape: "queue", label: "Queue" },
  { shape: "parallelogram", label: "Input / output" },
  { shape: "hexagon", label: "Preparation" },
  { shape: "document", label: "Document" },
  { shape: "subroutine", label: "Subroutine" },
  { shape: "cloud", label: "External" },
  { shape: "person", label: "Person" },
  { shape: "text", label: "Text" },
];

/** The outline of a shape in a w × h box, inset by half the stroke so it is never clipped. */
export function shapeOutline(shape: Shape, w: number, h: number, inset = 1): ReactNode {
  const [x0, y0, x1, y1] = [inset, inset, w - inset, h - inset];
  const W = x1 - x0;
  const H = y1 - y0;
  switch (shape) {
    case "rectangle":
      return <rect x={x0} y={y0} width={W} height={H} rx={3} />;
    case "rounded":
      return <rect x={x0} y={y0} width={W} height={H} rx={Math.min(14, H / 3)} />;
    case "terminal":
      return <rect x={x0} y={y0} width={W} height={H} rx={H / 2} />;
    case "diamond":
      return <polygon points={`${w / 2},${y0} ${x1},${h / 2} ${w / 2},${y1} ${x0},${h / 2}`} />;
    case "circle":
    case "ellipse":
      return <ellipse cx={w / 2} cy={h / 2} rx={W / 2} ry={H / 2} />;
    case "cylinder": {
      const ry = Math.min(12, H / 6);
      return (
        <>
          <path d={`M${x0},${y0 + ry} L${x0},${y1 - ry} A${W / 2},${ry} 0 0 0 ${x1},${y1 - ry} L${x1},${y0 + ry} A${W / 2},${ry} 0 0 0 ${x0},${y0 + ry} Z`} />
          <path className="shape-detail" d={`M${x0},${y0 + ry} A${W / 2},${ry} 0 0 0 ${x1},${y0 + ry}`} />
        </>
      );
    }
    case "queue": {
      const rx = Math.min(14, W / 8);
      return (
        <>
          <path d={`M${x0 + rx},${y0} L${x1 - rx},${y0} A${rx},${H / 2} 0 0 1 ${x1 - rx},${y1} L${x0 + rx},${y1} A${rx},${H / 2} 0 0 1 ${x0 + rx},${y0} Z`} />
          <path className="shape-detail" d={`M${x1 - rx},${y0} A${rx},${H / 2} 0 0 0 ${x1 - rx},${y1}`} />
        </>
      );
    }
    case "parallelogram": {
      const s = Math.min(18, W / 6);
      return <polygon points={`${x0 + s},${y0} ${x1},${y0} ${x1 - s},${y1} ${x0},${y1}`} />;
    }
    case "hexagon": {
      const s = Math.min(18, W / 6);
      return <polygon points={`${x0 + s},${y0} ${x1 - s},${y0} ${x1},${h / 2} ${x1 - s},${y1} ${x0 + s},${y1} ${x0},${h / 2}`} />;
    }
    case "document": {
      const wave = Math.min(10, H / 6);
      return <path d={`M${x0},${y0} L${x1},${y0} L${x1},${y1 - wave} C${x1 - W / 4},${y1 - 2.4 * wave} ${x0 + W / 2},${y1 + wave} ${x0},${y1 - wave} Z`} />;
    }
    case "subroutine":
      return (
        <>
          <rect x={x0} y={y0} width={W} height={H} rx={3} />
          <path className="shape-detail" d={`M${x0 + 10},${y0} L${x0 + 10},${y1} M${x1 - 10},${y0} L${x1 - 10},${y1}`} />
        </>
      );
    case "cloud": {
      // Five bumps around an ellipse-ish body, scaled to the box.
      const p = (px: number, py: number) => `${x0 + px * W},${y0 + py * H}`;
      return (
        <path d={`M${p(0.22, 0.86)} C${p(0.02, 0.86)} ${p(0.0, 0.52)} ${p(0.16, 0.46)} C${p(0.1, 0.18)} ${p(0.38, 0.06)} ${p(0.5, 0.22)} `
          + `C${p(0.58, 0.0)} ${p(0.88, 0.06)} ${p(0.86, 0.34)} C${p(1.02, 0.38)} ${p(1.04, 0.84)} ${p(0.8, 0.86)} Z`} />
      );
    }
    case "person": {
      const r = Math.min(14, W / 5);
      const cx = w / 2;
      return (
        <>
          <circle cx={cx} cy={y0 + r} r={r} />
          <path d={`M${cx - r * 1.9},${y0 + r * 4.4} C${cx - r * 1.9},${y0 + r * 2.4} ${cx + r * 1.9},${y0 + r * 2.4} ${cx + r * 1.9},${y0 + r * 4.4} Z`} />
        </>
      );
    }
    case "text":
      return null;
  }
}

/** A 20 × 16 icon of a shape, for pickers. */
export function ShapeIcon({ shape }: { shape: Shape }) {
  return (
    <svg className="shape-icon" viewBox="0 0 24 18" width="24" height="18" aria-hidden="true">
      <g fill="none" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round">
        {shape === "text" ? <path d="M6 5h12M12 5v9" /> : shapeOutline(shape, 24, 18, 1.5)}
      </g>
    </svg>
  );
}
