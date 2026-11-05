-- Walkthroughs: guided, step-by-step tours of a region. Canvas content, so versioned through the op log.
CREATE TABLE walkthroughs (
    id         TEXT PRIMARY KEY,
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    region_id  TEXT NOT NULL,
    title      TEXT NOT NULL,
    steps      TEXT NOT NULL,   -- JSON array of { text, highlight: [ids] }
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX walkthroughs_canvas ON walkthroughs(canvas_id);
