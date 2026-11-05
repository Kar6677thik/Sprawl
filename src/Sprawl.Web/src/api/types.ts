// Mirrors src/Sprawl.Contracts (camelCase JSON). Keep in step with the C# records.

export type CardKind = "idea" | "note" | "question" | "link" | "code" | "mermaid";
export type ConnectionKind = "relates" | "leads-to" | "contradicts" | "example-of" | "depends-on";
export type Placement = "auto" | "manual";
export type Author = "human" | "ai";
export type Shape = "rectangle" | "rounded" | "terminal" | "diamond" | "ellipse" | "circle" | "cylinder" | "parallelogram" | "hexagon"
  | "document" | "cloud" | "person" | "queue" | "subroutine" | "text";
export type RegionLayout = "cards" | "flow-down" | "flow-right";
export type LineStyle = "solid" | "dashed" | "dotted" | "thick";
export type Ends = "none" | "end" | "start" | "both";
export const isDiagram = (layout: RegionLayout | null | undefined) => layout === "flow-down" || layout === "flow-right";

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
  layout?: RegionLayout | null;
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
  shape?: Shape | null;
}

export interface Connection {
  id: string;
  sourceId: string;
  targetId: string;
  kind: ConnectionKind;
  label: string | null;
  createdBy: Author;
  createdAt: string;
  line?: LineStyle | null;
  ends?: Ends | null;
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
  sketches?: Sketch[] | null;
  walkthroughs?: Walkthrough[] | null;
}

export interface WalkthroughStep {
  text: string;
  highlight?: string[] | null;
}

/** A guided tour of a region: steps of explanation, each lighting up what it talks about. */
export interface Walkthrough {
  id: string;
  regionId: string;
  title: string;
  steps: WalkthroughStep[];
  createdBy: Author;
  createdAt: string;
  updatedAt: string;
}

/** A freehand stroke drawn by the owner; the path is relative to its box, the box to its region. */
export interface Sketch {
  id: string;
  regionId: string | null;
  path: string;
  tint: string | null;
  stroke: number;
  x: number;
  y: number;
  w: number;
  h: number;
  createdBy: Author;
  createdAt: string;
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
  | { type: "addRegion"; title: string; ref?: string; parent?: string; tint?: string; near?: string; layout?: RegionLayout }
  | { type: "addCard"; kind: CardKind; title: string; region?: string; ref?: string; body?: string; url?: string; language?: string; at?: { x: number; y: number }; shape?: Shape }
  | { type: "moveCards"; ids: string[]; toRegion: string }
  | { type: "updateCard"; id: string; title?: string; body?: string; kind?: CardKind; url?: string; language?: string; emphasis?: string; tint?: string; shape?: Shape | "none" }
  | { type: "connect"; from: string; to: string; kind?: ConnectionKind; label?: string; line?: LineStyle; ends?: Ends }
  | { type: "updateConnection"; id: string; kind?: ConnectionKind; label?: string; line?: LineStyle; ends?: Ends }
  | { type: "addDiagram"; title: string; mermaid: string; ref?: string; parent?: string; near?: string; layout?: RegionLayout; replace?: boolean }
  | { type: "disconnect"; id?: string; from?: string; to?: string; kind?: ConnectionKind }
  | { type: "deleteCards"; ids: string[] }
  | { type: "deleteRegion"; id: string }
  | { type: "updateRegion"; id: string; title?: string; tint?: string; layout?: RegionLayout }
  | { type: "updateCanvas"; title?: string; description?: string; tags?: string[]; collection?: string; clearCollection?: boolean; pinned?: boolean; archived?: boolean }
  | { type: "setLayout"; items: LayoutItem[] }
  | { type: "unpin"; ids: string[] }
  | { type: "addSketch"; path: string; x: number; y: number; w: number; h: number; region?: string; tint?: string; stroke?: number }
  | { type: "deleteSketches"; ids: string[] }
  | { type: "addWalkthrough"; region: string; title: string; steps: WalkthroughStep[]; ref?: string; replace?: boolean }
  | { type: "deleteWalkthrough"; id: string };

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

export type EntityKind = "canvas" | "region" | "card" | "connection" | "sketch" | "walkthrough" | "unknown";

export function kindOf(id: string): EntityKind {
  switch (id.slice(0, 3)) {
    case "cv_": return "canvas";
    case "rg_": return "region";
    case "cd_": return "card";
    case "cx_": return "connection";
    case "sk_": return "sketch";
    case "wt_": return "walkthrough";
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
  upcoming: DueQuestion[];
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

export interface ExternalCardRef {
  canvasId: string;
  canvasTitle: string;
  card: CardRef;
}

export interface LinkTarget {
  canvasId: string;
  canvasTitle: string;
  cardId: string;
  cardTitle: string;
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
  crossLinks: ExternalCardRef[];
  crossBacklinks: ExternalCardRef[];
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
