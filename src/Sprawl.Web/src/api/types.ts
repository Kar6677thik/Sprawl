// Mirrors src/Sprawl.Contracts (camelCase JSON). Keep in step with the C# records.

export type CardKind = "idea" | "note" | "question" | "link" | "code";
export type ConnectionKind = "relates" | "leads-to" | "contradicts" | "example-of" | "depends-on";
export type Placement = "auto" | "manual";
export type Author = "human" | "ai";

export interface CanvasMeta {
  id: string;
  title: string;
  description: string;
  tags: string[];
  collection: string | null;
  pinned: boolean;
  archived: boolean;
  createdAt: string;
}

export interface Region {
  id: string;
  parentId: string | null;
  title: string;
  tint: string | null;
  cols: number;
  x: number;
  y: number;
  w: number;
  h: number;
  placement: Placement;
  createdBy: Author;
  createdAt: string;
  updatedAt: string;
}

export interface Card {
  id: string;
  regionId: string | null;
  kind: CardKind;
  title: string;
  body: string;
  url: string | null;
  language: string | null;
  tint: string | null;
  emphasis: "normal" | "key";
  size: "compact" | "regular" | "tall";
  x: number;
  y: number;
  w: number;
  h: number;
  placement: Placement;
  sizeSource: Placement;
  createdBy: Author;
  updatedBy: Author;
  createdAt: string;
  updatedAt: string;
}

export interface Connection {
  id: string;
  sourceId: string;
  targetId: string;
  kind: ConnectionKind;
  label: string | null;
  createdBy: Author;
  createdAt: string;
}

export interface LayoutWarning {
  code: string;
  firstId: string;
  secondId: string;
}

export interface CanvasDocument {
  meta: CanvasMeta;
  version: number;
  updatedAt: string;
  regions: Region[];
  cards: Card[];
  connections: Connection[];
  layoutWarnings: LayoutWarning[];
}

export interface CanvasSummary {
  id: string;
  title: string;
  description: string;
  tags: string[];
  collection: string | null;
  pinned: boolean;
  archived: boolean;
  version: number;
  cardCount: number;
  regionCount: number;
  openQuestions: number;
  createdAt: string;
  updatedAt: string;
  deletedAt: string | null;
}

export interface OpRecord {
  type: string;
  entityId: string;
  before: unknown;
  after: unknown;
}

export interface CanvasEvent {
  canvasId: string;
  version: number;
  kind: string;
  actor: "owner" | "mcp";
  client: string | null;
  summary: string;
  ops: OpRecord[];
  createdAt: string;
}

export interface SkippedItem {
  reason: string;
  ref: string | null;
  id: string | null;
  existingId: string | null;
}

export interface BatchResult {
  canvasId: string;
  version: number;
  changed: boolean;
  summary: string;
  refs: Record<string, string>;
  skipped: SkippedItem[];
  warnings: string[];
  replayed: boolean;
}

export interface LayoutItem {
  id: string;
  x: number;
  y: number;
  w?: number;
  h?: number;
  container?: string;
  asAuto?: boolean;
}

export type Intent =
  | { type: "addRegion"; title: string; ref?: string; parent?: string; tint?: string; near?: string }
  | { type: "addCard"; kind: CardKind; title: string; region?: string; ref?: string; body?: string; url?: string; language?: string; at?: { x: number; y: number } }
  | { type: "moveCards"; ids: string[]; toRegion: string }
  | { type: "updateCard"; id: string; title?: string; body?: string; kind?: CardKind; url?: string; language?: string; emphasis?: string; tint?: string }
  | { type: "connect"; from: string; to: string; kind?: ConnectionKind; label?: string }
  | { type: "disconnect"; id?: string; from?: string; to?: string; kind?: ConnectionKind }
  | { type: "deleteCards"; ids: string[] }
  | { type: "deleteRegion"; id: string }
  | { type: "updateRegion"; id: string; title?: string; tint?: string }
  | { type: "updateCanvas"; title?: string; description?: string; tags?: string[]; pinned?: boolean; archived?: boolean }
  | { type: "setLayout"; items: LayoutItem[] }
  | { type: "unpin"; ids: string[] };

export interface BatchRequest {
  intents: Intent[];
  requestId?: string;
  kind?: "edit" | "tidy";
}

export interface HistoryEntry {
  version: number;
  kind: string;
  actor: "owner" | "mcp";
  client: string | null;
  summary: string;
  revertsTo: number | null;
  undoes: number | null;
  opCount: number;
  createdAt: string;
}

export interface CardSearchHit {
  canvasId: string;
  canvasTitle: string;
  cardId: string;
  regionId: string | null;
  regionTitle: string | null;
  kind: CardKind;
  title: string;
  snippet: string;
}

export interface SearchResult {
  canvases: { canvasId: string; title: string; snippet: string }[];
  cards: CardSearchHit[];
}

export interface Session {
  signedIn: boolean;
  passwordSignIn: boolean;
  secondFactor: "totp" | null;
  devSignIn: boolean;
}

export type EntityKind = "canvas" | "region" | "card" | "connection" | "unknown";

export function kindOf(id: string): EntityKind {
  switch (id.slice(0, 3)) {
    case "cv_": return "canvas";
    case "rg_": return "region";
    case "cd_": return "card";
    case "cx_": return "connection";
    default: return "unknown";
  }
}

export type ReviewGrade = "again" | "hard" | "good" | "easy";

export interface ReviewState {
  reps: number;
  ease: number;
  intervalDays: number;
  dueAt: string;
  lastReviewedAt: string | null;
}

export interface DueQuestion {
  canvasId: string;
  canvasTitle: string;
  card: CardRef;
  body: string;
  review: ReviewState;
}

export interface ReviewQueue {
  dueCount: number;
  due: DueQuestion[];
}

export interface CardRef {
  id: string;
  title: string;
  kind: CardKind;
  regionId: string | null;
  regionTitle: string | null;
}

export interface RelatedCard {
  card: CardRef;
  score: number;
  sharedTerms: string[];
}

export interface CardContext {
  canvasId: string;
  version: number;
  card: CardRef;
  tags: string[];
  links: CardRef[];
  unresolvedLinks: string[];
  backlinks: CardRef[];
  mentions: CardRef[];
  related: RelatedCard[];
}

export interface RegionGap {
  firstId: string;
  firstTitle: string;
  secondId: string;
  secondTitle: string;
  similarity: number;
  sharedTerms: string[];
}

export interface CanvasInsights {
  canvasId: string;
  version: number;
  cards: number;
  regions: number;
  connections: number;
  textLinks: number;
  gaps: RegionGap[];
  orphans: CardRef[];
  danglingLinks: { target: string; from: CardRef[] }[];
  openQuestions: CardRef[];
  contradictions: { connectionId: string; fromId: string; fromTitle: string; toId: string; toTitle: string; label: string | null }[];
  hubs: { card: CardRef; degree: number }[];
  tags: { tag: string; count: number }[];
}
