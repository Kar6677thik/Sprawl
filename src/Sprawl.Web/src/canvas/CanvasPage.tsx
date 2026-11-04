import { useMemo } from "react";
import { CanvasView } from "./CanvasView";
import { useCanvas } from "./useCanvas";
import { navigate } from "../router";
import { ThemeButton } from "../theme/ThemeButton";

export function CanvasPage({ id }: { id: string }) {
  const canvas = useCanvas(id);
  const { doc } = canvas;
  // "?focus=cd_…" comes from search results on another canvas: open straight at that card.
  const initialFocus = useMemo(() => new URLSearchParams(location.search).get("focus"), []);
  return (
    <div className="canvas-page">
      <header className="bar">
        <a href="/" className="bar-back" onClick={(event) => { event.preventDefault(); navigate("/"); }} aria-label="All canvases">←</a>
        <div className="bar-title">
          <h1>{doc?.meta.title ?? "Loading…"}</h1>
          {doc && (
            <span className="bar-meta">
              v{doc.version} · {doc.cards.length} cards · {doc.cards.filter((card) => card.kind === "question").length} open questions
            </span>
          )}
        </div>
        <span className="bar-hint"><kbd>Ctrl</kbd><kbd>K</kbd></span>
        <span className={`live-dot${canvas.live ? " is-live" : ""}`} title={canvas.live ? "Live" : "Reconnecting…"} />
        <ThemeButton />
      </header>
      {canvas.error && !doc && <p className="empty">{canvas.error}</p>}
      {doc && <CanvasView canvas={canvas} initialFocus={initialFocus} />}
    </div>
  );
}
