import { useEffect, useRef, useState } from "react";
import { api } from "../api/client";
import type { CanvasDocument } from "../api/types";

interface Props {
  canvasId: string;
  latest: number;
  /** The past state to draw, or null to draw the live canvas. */
  onState: (doc: CanvasDocument | null) => void;
  onRestore: (version: number) => void;
  onExit: () => void;
}

const STEP_MS = 450;

/**
 * Watch the canvas grow: a slider over every version, with play. Past states come from the op log
 * (`GET /state?version=`), are cached, and are read-only. Restore reverts to the version shown, as a new version.
 */
export function Replay({ canvasId, latest, onState, onRestore, onExit }: Props) {
  const [version, setVersion] = useState(latest);
  const [playing, setPlaying] = useState(false);
  const cache = useRef(new Map<number, CanvasDocument>());
  const wanted = useRef(version);

  useEffect(() => {
    wanted.current = version;
    if (version >= latest) { onState(null); return; }
    const cached = cache.current.get(version);
    if (cached) { onState(cached); return; }
    const timer = window.setTimeout(() => {
      api.stateAt(canvasId, version).then((doc) => {
        cache.current.set(version, doc);
        if (wanted.current === version) onState(doc);
      }, () => setPlaying(false));
    }, playing ? 0 : 80);
    return () => window.clearTimeout(timer);
  }, [canvasId, latest, onState, playing, version]);

  useEffect(() => {
    if (!playing) return;
    if (version >= latest) { setPlaying(false); return; }
    const timer = window.setTimeout(() => setVersion((v) => Math.min(latest, v + 1)), STEP_MS);
    return () => window.clearTimeout(timer);
  }, [latest, playing, version]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.ctrlKey || event.metaKey || event.altKey) return;
      const handled = ["ArrowLeft", "ArrowRight", " ", "Escape", "Home", "End"];
      if (!handled.includes(event.key)) return;
      event.preventDefault();
      event.stopPropagation();
      if (event.key === "Escape") onExit();
      else if (event.key === " ") setPlaying((p) => { if (!p && version >= latest) setVersion(1); return !p; });
      else if (event.key === "Home") setVersion(1);
      else if (event.key === "End") setVersion(latest);
      else { setPlaying(false); setVersion((v) => Math.max(1, Math.min(latest, v + (event.key === "ArrowRight" ? 1 : -1)))); }
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [latest, onExit, version]);

  useEffect(() => () => onState(null), [onState]);

  return (
    <div className="replay" role="group" aria-label="History replay">
      <button type="button" className="icon-button" aria-label={playing ? "Pause" : "Play"}
        onClick={() => { if (!playing && version >= latest) setVersion(1); setPlaying(!playing); }}>
        <svg viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          {playing ? <path d="M3 2h2v8H3zM7 2h2v8H7z" fill="currentColor" /> : <path d="M3 1.5v9l7.5-4.5z" fill="currentColor" />}
        </svg>
      </button>
      <input type="range" min={1} max={latest} value={version} aria-label="Version"
        onChange={(e) => { setPlaying(false); setVersion(Number(e.target.value)); }} />
      <span className="replay-version">v{version} / {latest}</span>
      <button type="button" className="quiet small" disabled={version >= latest} onClick={() => onRestore(version)} title="Revert the canvas to this version (as a new version; nothing is lost)">Restore</button>
      <button type="button" className="quiet small" onClick={onExit}>Done <kbd>Esc</kbd></button>
    </div>
  );
}
