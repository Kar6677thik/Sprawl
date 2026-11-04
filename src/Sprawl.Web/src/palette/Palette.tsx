import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../api/client";
import type { CanvasDocument, CanvasSummary, CardSearchHit } from "../api/types";
import { rank } from "./fuzzy";

export interface PaletteEntry {
  id: string;
  group: string;
  label: string;
  hint?: string;
  keys?: string;
  run: () => void;
}

export type PaletteMode = "all" | "canvas" | "global";

interface Props {
  mode: PaletteMode;
  doc: CanvasDocument | null;
  actions: PaletteEntry[];
  onOpenCard: (id: string) => void;
  onFocus: (id: string) => void;
  onOpenCanvas: (canvasId: string, focus?: string) => void;
  onClose: () => void;
}

const PLACEHOLDER: Record<PaletteMode, string> = {
  all: "Type a command, region, card or canvas…",
  canvas: "Search this canvas…",
  global: "Search all canvases…",
};

/** Ctrl+K. Everything is reachable by name: actions, this canvas's regions and cards, other canvases, full-text search. */
export function Palette({ mode, doc, actions, onOpenCard, onFocus, onOpenCanvas, onClose }: Props) {
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [canvases, setCanvases] = useState<CanvasSummary[]>([]);
  const [hits, setHits] = useState<CardSearchHit[]>([]);
  const list = useRef<HTMLUListElement>(null);

  useEffect(() => { if (mode === "all") api.listCanvases().then(setCanvases, () => setCanvases([])); }, [mode]);
  useEffect(() => {
    if (mode !== "global" || query.trim().length < 2) { setHits([]); return; }
    const timer = window.setTimeout(() => api.search(query).then((r) => setHits(r.cards), () => setHits([])), 150);
    return () => window.clearTimeout(timer);
  }, [mode, query]);

  const entries = useMemo<PaletteEntry[]>(() => {
    if (mode === "global")
      return hits.map((hit) => ({
        id: hit.cardId, group: hit.canvasTitle, label: hit.title, hint: hit.snippet.replace(/[«»]/g, ""),
        run: () => onOpenCanvas(hit.canvasId, hit.cardId),
      }));
    const regions = (doc?.regions ?? []).map((r) => ({ id: r.id, group: "Regions", label: r.title, hint: "focus", run: () => onFocus(r.id) }));
    const cards = (doc?.cards ?? []).map((c) => ({
      id: c.id, group: "Cards", label: c.title, hint: c.kind === "question" ? "open question" : c.kind,
      run: () => onOpenCard(c.id), searchText: `${c.title} ${mode === "canvas" ? c.body : ""}`,
    }));
    if (mode === "canvas") {
      if (!query.trim()) return cards.slice(0, 50);
      return rank(query, cards, (entry) => entry.searchText, 50);
    }
    const others = canvases.filter((c) => c.id !== doc?.meta.id).map((c) => ({ id: c.id, group: "Canvases", label: c.title, hint: `${c.cardCount} cards`, run: () => onOpenCanvas(c.id) }));
    if (!query.trim()) return [...actions, ...regions.slice(0, 8), ...others.slice(0, 8)];
    return [
      ...rank(query, actions, (a) => a.label, 8),
      ...rank(query, regions, (r) => r.label, 8),
      ...rank(query, cards, (c) => c.label, 12),
      ...rank(query, others, (c) => c.label, 8),
    ];
  }, [actions, canvases, doc, hits, mode, onFocus, onOpenCanvas, onOpenCard, query]);

  useEffect(() => setActive(0), [query, mode]);
  useEffect(() => { list.current?.querySelector(".is-active")?.scrollIntoView({ block: "nearest" }); }, [active]);

  const run = (entry: PaletteEntry | undefined) => { if (entry) { onClose(); entry.run(); } };

  return (
    <div className="overlay" onPointerDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="palette" role="dialog" aria-modal="true" aria-label="Command palette">
        <input
          autoFocus value={query} placeholder={PLACEHOLDER[mode]} aria-label={PLACEHOLDER[mode]} role="combobox" aria-expanded="true" aria-controls="palette-list"
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Escape") { e.preventDefault(); onClose(); }
            else if (e.key === "ArrowDown") { e.preventDefault(); setActive((i) => Math.min(i + 1, entries.length - 1)); }
            else if (e.key === "ArrowUp") { e.preventDefault(); setActive((i) => Math.max(i - 1, 0)); }
            else if (e.key === "Enter") { e.preventDefault(); run(entries[active]); }
          }}
        />
        <ul className="palette-list" id="palette-list" role="listbox" ref={list}>
          {entries.map((entry, index) => (
            <li key={`${entry.group}:${entry.id}`} role="option" aria-selected={index === active} className={index === active ? "is-active" : ""}
              onPointerMove={() => setActive(index)} onClick={() => run(entry)}>
              {(index === 0 || entries[index - 1]!.group !== entry.group) && <span className="palette-group">{entry.group}</span>}
              <span className="palette-label">{entry.label}</span>
              {entry.hint && <span className="palette-hint">{entry.hint}</span>}
              {entry.keys && <kbd>{entry.keys}</kbd>}
            </li>
          ))}
          {entries.length === 0 && <li className="palette-empty">{mode === "global" && query.trim().length < 2 ? "Type at least two letters." : "No matches."}</li>}
        </ul>
      </div>
    </div>
  );
}
