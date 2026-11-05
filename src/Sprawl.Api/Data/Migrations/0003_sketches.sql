-- Freehand strokes drawn by the owner. Canvas content like cards, so versioned through the op log.
CREATE TABLE sketches (
    id         TEXT PRIMARY KEY,
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    region_id  TEXT NULL,
    path       TEXT NOT NULL,   -- SVG path data (M/L/Q/C/Z and numbers only), relative to the box
    tint       TEXT NULL,
    stroke     INTEGER NOT NULL,
    x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX sketches_canvas ON sketches(canvas_id);
