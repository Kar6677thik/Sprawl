import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { api } from "../api/client";
import type { CanvasSummary } from "../api/types";
import { navigate } from "../router";
import { ThemeButton } from "../theme/ThemeButton";

/** Canvases without a collection first (most recent first, as listed), then each collection alphabetically. */
function groups(canvases: CanvasSummary[]): [string | null, CanvasSummary[]][] {
  const map = new Map<string | null, CanvasSummary[]>();
  for (const canvas of canvases) {
    const list = map.get(canvas.collection) ?? [];
    list.push(canvas);
    map.set(canvas.collection, list);
  }
  return [...map.entries()].sort(([a], [b]) => (a === null ? -1 : b === null ? 1 : a.localeCompare(b)));
}

export function Home({ onSignOut }: { onSignOut: () => void }) {
  const [canvases, setCanvases] = useState<CanvasSummary[] | null>(null);
  const [query, setQuery] = useState("");
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [due, setDue] = useState<Map<string, number>>(new Map());

  useEffect(() => {
    api.reviews(undefined, 100).then((queue) => {
      const counts = new Map<string, number>();
      for (const item of queue.due) counts.set(item.canvasId, (counts.get(item.canvasId) ?? 0) + 1);
      setDue(counts);
    }, () => setDue(new Map()));
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      api.listCanvases(query ? { q: query } : {}).then(setCanvases, (failure: Error) => setError(failure.message));
    }, query ? 150 : 0);
    return () => window.clearTimeout(timer);
  }, [query]);

  const file = useRef<HTMLInputElement>(null);

  /** A JSON Canvas file (Obsidian's .canvas) becomes a new canvas, named after the file. */
  const importFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const chosen = event.target.files?.[0];
    event.target.value = "";
    if (!chosen) return;
    try {
      const canvas: unknown = JSON.parse(await chosen.text());
      const name = chosen.name.replace(/\.(canvas|json)$/i, "").trim().slice(0, 120) || "Imported canvas";
      const result = await api.importCanvas(name, canvas);
      navigate(`/c/${result.canvasId}`);
    } catch (failure) {
      setError(failure instanceof SyntaxError ? "That file is not JSON Canvas." : failure instanceof Error ? failure.message : "The import failed.");
    }
  };

  const create = async (event: FormEvent) => {
    event.preventDefault();
    if (!title.trim()) return;
    try {
      const result = await api.createCanvas(title.trim());
      navigate(`/c/${result.canvasId}`);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "The canvas could not be created.");
    }
  };

  return (
    <div className="home">
      <header className="bar">
        <h1 className="wordmark">Sprawl</h1>
        <input className="search" type="search" placeholder="Filter canvases" value={query} onChange={(e) => setQuery(e.target.value)} aria-label="Filter canvases" />
        <ThemeButton />
        <button type="button" className="quiet" onClick={() => void api.signOut().then(onSignOut)}>Sign out</button>
      </header>
      <main className="home-main">
        <form className="new-canvas" onSubmit={create}>
          <input placeholder="New canvas: a topic to map" value={title} onChange={(e) => setTitle(e.target.value)} aria-label="New canvas title" maxLength={120} />
          <button type="submit" className="primary" disabled={!title.trim()}>Create</button>
          <button type="button" className="quiet" onClick={() => file.current?.click()} title="Import a JSON Canvas (.canvas) file, e.g. from Obsidian">Import…</button>
          <input ref={file} type="file" accept=".canvas,.json,application/json" hidden onChange={(e) => void importFile(e)} />
        </form>
        {error && <p className="error" role="alert">{error}</p>}
        {canvases && canvases.length === 0 && <p className="empty">{query ? "No canvases match." : "No canvases yet. Create one, or ask Claude to."}</p>}
        {groups(canvases ?? []).map(([collection, list]) => (
        <section key={collection ?? ""} className="collection">
        {collection && <h2 className="collection-title">{collection} <span>{list.length}</span></h2>}
        <ul className="canvas-list">
          {list.map((canvas) => (
            <li key={canvas.id}>
              <a href={`/c/${canvas.id}`} onClick={(event) => { event.preventDefault(); navigate(`/c/${canvas.id}`); }}>
                <span className="canvas-title">{canvas.pinned && <span className="pin" aria-label="Pinned" />}{canvas.title}</span>
                {canvas.description && <span className="canvas-description">{canvas.description}</span>}
                <span className="canvas-meta">
                  {canvas.cardCount} cards · {canvas.regionCount} regions{canvas.openQuestions > 0 ? ` · ${canvas.openQuestions} open questions` : ""}
                  {due.get(canvas.id) ? <span className="due"> · {due.get(canvas.id)} due for review</span> : null}
                  {canvas.tags.length > 0 && <> · {canvas.tags.map((tag) => <span key={tag} className="tag">{tag}</span>)}</>}
                </span>
              </a>
            </li>
          ))}
        </ul>
        </section>
        ))}
      </main>
    </div>
  );
}
