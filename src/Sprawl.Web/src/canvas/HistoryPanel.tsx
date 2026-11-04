import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { HistoryEntry } from "../api/types";

interface Props {
  canvasId: string;
  version: number;
  onUndo: (version: number) => void;
  onRevert: (version: number) => void;
  onClose: () => void;
}

/** How the canvas grew: every version, who made it, and a way back. Undo and revert both add new versions. */
export function HistoryPanel({ canvasId, version, onUndo, onRevert, onClose }: Props) {
  const [entries, setEntries] = useState<HistoryEntry[] | null>(null);
  useEffect(() => { api.history(canvasId).then(setEntries, () => setEntries([])); }, [canvasId, version]);
  return (
    <aside className="panel" aria-label="History">
      <header className="panel-header">
        <span className="panel-title">History</span>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
      </header>
      <ol className="history">
        {entries?.map((entry) => (
          <li key={entry.version} className={entry.version === version ? "is-current" : ""}>
            <div className="history-line">
              <span className="history-version">v{entry.version}</span>
              <span className={`history-who${entry.actor === "mcp" ? " is-ai" : ""}`}>{who(entry)}</span>
              <time dateTime={entry.createdAt} title={new Date(entry.createdAt).toLocaleString()}>{ago(entry.createdAt)}</time>
            </div>
            <p className="history-summary">{entry.summary}</p>
            {entry.version !== version && (
              <div className="history-actions">
                {entry.kind !== "create" && entry.kind !== "trash" && entry.kind !== "restore" && entry.opCount > 0 && (
                  <button type="button" className="quiet" onClick={() => onUndo(entry.version)}>Undo this</button>
                )}
                <button type="button" className="quiet" onClick={() => { if (window.confirm(`Return the canvas to version ${entry.version}? This adds a new version; nothing is lost.`)) onRevert(entry.version); }}>
                  Revert to here
                </button>
              </div>
            )}
          </li>
        ))}
      </ol>
    </aside>
  );
}

function who(entry: HistoryEntry) {
  if (entry.actor === "owner") return "You";
  if (!entry.client) return "Claude";
  try { return `Claude · ${new URL(entry.client).host}`; } catch { return `Claude · ${entry.client}`; }
}

export function ago(iso: string, now = Date.now()) {
  const seconds = Math.max(0, Math.round((now - Date.parse(iso)) / 1000));
  if (seconds < 60) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return days < 30 ? `${days}d ago` : new Date(iso).toLocaleDateString();
}
