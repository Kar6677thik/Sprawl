-- Drill-down: a card may name a region that explains it in detail (zooming into the card opens it).
ALTER TABLE cards ADD COLUMN detail TEXT NULL;
