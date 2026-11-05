-- Review schedule for open questions. Not canvas content, so not in the op log. A row outlives its card's deletion,
-- so an undo brings the schedule back; the canvas cascade removes it on purge.
CREATE TABLE reviews (
    card_id          TEXT PRIMARY KEY,
    canvas_id        TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    reps             INTEGER NOT NULL,
    ease             REAL NOT NULL,
    interval_days    REAL NOT NULL,
    due_at           TEXT NOT NULL,
    last_reviewed_at TEXT NULL
);
CREATE INDEX reviews_canvas ON reviews(canvas_id);
