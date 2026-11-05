export const SHORTCUTS: [string, string][] = [
  ["Ctrl K", "Command palette (type “collection: Name” to file this canvas)"],
  ["/", "Search this canvas"],
  ["Ctrl Shift F", "Search all canvases"],
  ["#", "Browse cards by #tag"],
  ["I", "Insights: gaps, orphans, links with no card"],
  ["N", "New card (in the selected region, or under the cursor)"],
  ["Shift N", "New region"],
  ["Enter", "Open the selected card"],
  ["C", "Connect the selection to the next card or region you click"],
  ["Delete", "Delete the selected cards or connections"],
  ["T", "Tidy the selected region, or the whole canvas"],
  ["Ctrl Z / Ctrl Shift Z", "Undo / redo your last change"],
  ["Shift I", "Capture to the Inbox (stays open for the next thought)"],
  ["R", "Review open questions (1-4 to grade)"],
  ["H", "History"],
  ["Shift H", "Replay history: slider, Space plays, arrows step"],
  ["D", "Draw: freehand sketch (1-6 colour, [ ] width, Esc to stop)"],
  ["P", "Present the regions one by one (arrows to move, Esc to stop)"],
  ["F / Shift F", "Focus the selection / fit the canvas"],
  ["1 2 3", "Zoom out to the map / titles / bodies"],
  ["Esc", "Close or cancel"],
  ["?", "This sheet"],
];

export function Shortcuts({ onClose }: { onClose: () => void }) {
  return (
    <div className="overlay" onPointerDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="sheet" role="dialog" aria-modal="true" aria-label="Keyboard shortcuts">
        <header className="panel-header">
          <span className="panel-title">Keyboard</span>
          <button type="button" className="icon-button" autoFocus onClick={onClose} aria-label="Close">×</button>
        </header>
        <dl className="shortcuts">
          {SHORTCUTS.map(([keys, action]) => (
            <div key={keys}>
              <dt>{keys.split(" ").map((key) => <kbd key={key}>{key}</kbd>)}</dt>
              <dd>{action}</dd>
            </div>
          ))}
        </dl>
      </div>
    </div>
  );
}
