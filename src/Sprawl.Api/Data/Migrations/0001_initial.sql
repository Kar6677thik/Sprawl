-- Current state: one row per live entity. Deleted entities live on only in the op log.
CREATE TABLE canvases (
    id          TEXT PRIMARY KEY,
    title       TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    tags        TEXT NOT NULL DEFAULT '[]',   -- JSON array of lowercase slugs
    collection  TEXT NULL,
    pinned      INTEGER NOT NULL DEFAULT 0,
    archived    INTEGER NOT NULL DEFAULT 0,
    version     INTEGER NOT NULL DEFAULT 0,  -- number of committed batches
    deleted_at  TEXT NULL,                   -- trash; purged after 30 days
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE TABLE regions (
    id         TEXT PRIMARY KEY,
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    parent_id  TEXT NULL,
    title      TEXT NOT NULL,
    tint       TEXT NULL,
    cols       INTEGER NOT NULL,
    x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
    placement  TEXT NOT NULL,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX regions_canvas ON regions(canvas_id);

CREATE TABLE cards (
    id          TEXT PRIMARY KEY,
    canvas_id   TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    region_id   TEXT NULL,
    kind        TEXT NOT NULL,
    title       TEXT NOT NULL,
    body        TEXT NOT NULL,
    url         TEXT NULL,
    language    TEXT NULL,
    tint        TEXT NULL,
    emphasis    TEXT NOT NULL,
    size        TEXT NOT NULL,
    x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
    placement   TEXT NOT NULL,
    size_source TEXT NOT NULL,
    created_by  TEXT NOT NULL,
    updated_by  TEXT NOT NULL,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);
CREATE INDEX cards_canvas ON cards(canvas_id);

CREATE TABLE connections (
    id         TEXT PRIMARY KEY,
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    source_id  TEXT NOT NULL,
    target_id  TEXT NOT NULL,
    kind       TEXT NOT NULL,
    label      TEXT NULL,
    created_by TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX connections_canvas ON connections(canvas_id);

-- History: one batch per version, one op per changed entity, full before/after images as JSON.
CREATE TABLE batches (
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    version    INTEGER NOT NULL,
    kind       TEXT NOT NULL,
    actor      TEXT NOT NULL,
    client     TEXT NULL,
    summary    TEXT NOT NULL,
    request_id TEXT NULL,
    reverts_to INTEGER NULL,
    undoes     INTEGER NULL,
    op_count   INTEGER NOT NULL,
    result     TEXT NOT NULL,  -- the BatchResult returned, replayed for a repeated request_id
    created_at TEXT NOT NULL,
    PRIMARY KEY (canvas_id, version)
);
CREATE UNIQUE INDEX batches_request ON batches(canvas_id, request_id) WHERE request_id IS NOT NULL;
CREATE INDEX batches_request_any ON batches(request_id) WHERE request_id IS NOT NULL;

CREATE TABLE ops (
    canvas_id   TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    version     INTEGER NOT NULL,
    seq         INTEGER NOT NULL,
    type        TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    before_json TEXT NULL,
    after_json  TEXT NULL,
    PRIMARY KEY (canvas_id, version, seq)
);

CREATE TABLE snapshots (
    canvas_id  TEXT NOT NULL REFERENCES canvases(id) ON DELETE CASCADE,
    version    INTEGER NOT NULL,
    state      BLOB NOT NULL,  -- gzip JSON of the whole canvas after this version
    created_at TEXT NOT NULL,
    PRIMARY KEY (canvas_id, version)
);

-- Full-text search, kept in step with the content tables by triggers inside each write transaction.
CREATE VIRTUAL TABLE cards_fts USING fts5(title, body, url, content='cards', content_rowid='rowid', tokenize='unicode61 remove_diacritics 2');
CREATE TRIGGER cards_fts_insert AFTER INSERT ON cards BEGIN
    INSERT INTO cards_fts(rowid, title, body, url) VALUES (new.rowid, new.title, new.body, coalesce(new.url, ''));
END;
CREATE TRIGGER cards_fts_delete AFTER DELETE ON cards BEGIN
    INSERT INTO cards_fts(cards_fts, rowid, title, body, url) VALUES ('delete', old.rowid, old.title, old.body, coalesce(old.url, ''));
END;
CREATE TRIGGER cards_fts_update AFTER UPDATE OF title, body, url ON cards BEGIN
    INSERT INTO cards_fts(cards_fts, rowid, title, body, url) VALUES ('delete', old.rowid, old.title, old.body, coalesce(old.url, ''));
    INSERT INTO cards_fts(rowid, title, body, url) VALUES (new.rowid, new.title, new.body, coalesce(new.url, ''));
END;

CREATE VIRTUAL TABLE canvases_fts USING fts5(title, description, tags, content='canvases', content_rowid='rowid', tokenize='unicode61 remove_diacritics 2');
CREATE TRIGGER canvases_fts_insert AFTER INSERT ON canvases BEGIN
    INSERT INTO canvases_fts(rowid, title, description, tags) VALUES (new.rowid, new.title, new.description, new.tags);
END;
CREATE TRIGGER canvases_fts_delete AFTER DELETE ON canvases BEGIN
    INSERT INTO canvases_fts(canvases_fts, rowid, title, description, tags) VALUES ('delete', old.rowid, old.title, old.description, old.tags);
END;
CREATE TRIGGER canvases_fts_update AFTER UPDATE OF title, description, tags ON canvases BEGIN
    INSERT INTO canvases_fts(canvases_fts, rowid, title, description, tags) VALUES ('delete', old.rowid, old.title, old.description, old.tags);
    INSERT INTO canvases_fts(rowid, title, description, tags) VALUES (new.rowid, new.title, new.description, new.tags);
END;
