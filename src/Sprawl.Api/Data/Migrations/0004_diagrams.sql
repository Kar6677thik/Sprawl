-- Diagrams: a region may lay out as a flow, a card may be a shape, a connection may carry a line style and arrowheads.
ALTER TABLE regions ADD COLUMN layout TEXT NULL;        -- null (cards), flow-down, flow-right
ALTER TABLE cards ADD COLUMN shape TEXT NULL;           -- null (a card), or a shape name
ALTER TABLE connections ADD COLUMN line TEXT NULL;      -- null (solid), dashed, dotted, thick
ALTER TABLE connections ADD COLUMN ends TEXT NULL;      -- null (by kind), none, end, start, both
