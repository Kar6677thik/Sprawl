import type { BatchRequest, BatchResult, CanvasDocument, CanvasInsights, CardContext, LinkTarget, ReviewGrade, ReviewQueue, ReviewState, CanvasSummary, HistoryEntry, SearchResult, Session } from "./types";

/** A refused request, carrying the API's stable problem `code` (see ErrorCodes in Sprawl.Contracts). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly currentVersion: number | null = null,
    readonly entities: string[] = [],
  ) {
    super(message);
    this.name = "ApiError";
  }
}

type Fetch = typeof fetch;

/**
 * Thin client for /api/v1. Every unsafe request carries `X-Sprawl-Request: 1` (the API's cross-site guard) and,
 * when the caller knows the version it based its change on, `If-Match`.
 */
export class SprawlClient {
  constructor(private readonly fetcher: Fetch = (input, init) => fetch(input, init)) {}

  session = () => this.json<Session>("GET", "/api/v1/session");
  signIn = (password: string, code?: string) => this.json<void>("POST", "/api/v1/session", { password, code });
  devSignIn = () => this.json<void>("POST", "/api/v1/session/dev");
  signOut = () => this.json<void>("DELETE", "/api/v1/session");

  listCanvases = (query: { q?: string; tag?: string; archived?: boolean } = {}) => {
    const params = new URLSearchParams();
    if (query.q) params.set("q", query.q);
    if (query.tag) params.set("tag", query.tag);
    if (query.archived) params.set("archived", "true");
    const suffix = params.size > 0 ? `?${params}` : "";
    return this.json<CanvasSummary[]>("GET", `/api/v1/canvases${suffix}`);
  };

  createCanvas = (title: string, description?: string) =>
    this.json<BatchResult>("POST", "/api/v1/canvases", { title, description, requestId: crypto.randomUUID() });

  canvas = (id: string) => this.json<CanvasDocument>("GET", `/api/v1/canvases/${encodeURIComponent(id)}`);

  batch = (id: string, request: BatchRequest, ifMatch?: number) =>
    this.json<BatchResult>("POST", `/api/v1/canvases/${encodeURIComponent(id)}/batches`, { requestId: crypto.randomUUID(), ...request }, ifMatch);

  resolveLink = (id: string, target: string) =>
    this.json<LinkTarget>("GET", `/api/v1/canvases/${encodeURIComponent(id)}/links/resolve?target=${encodeURIComponent(target)}`);

  stateAt = (id: string, version: number) => this.json<CanvasDocument>("GET", `/api/v1/canvases/${encodeURIComponent(id)}/state?version=${version}`);

  insights = (id: string) => this.json<CanvasInsights>("GET", `/api/v1/canvases/${encodeURIComponent(id)}/insights`);

  cardContext = (id: string, cardId: string) =>
    this.json<CardContext>("GET", `/api/v1/canvases/${encodeURIComponent(id)}/cards/${encodeURIComponent(cardId)}/context`);

  /** A same-origin download URL (the session cookie authorizes it). */
  exportUrl = (id: string, format: "markdown" | "json-canvas") => `/api/v1/canvases/${encodeURIComponent(id)}/export?format=${format}`;

  exportMarkdown = async (id: string) => {
    const response = await this.fetcher(this.exportUrl(id, "markdown"), { credentials: "same-origin", headers: { Accept: "text/markdown" } });
    if (!response.ok) throw toError(response.status, await response.text());
    return response.text();
  };

  importCanvas = (title: string, canvas: unknown) =>
    this.json<BatchResult>("POST", "/api/v1/canvases/import", { title, canvas, requestId: crypto.randomUUID() });

  reviews = (canvas?: string, limit = 50) =>
    this.json<ReviewQueue>("GET", `/api/v1/reviews?limit=${limit}${canvas ? `&canvas=${encodeURIComponent(canvas)}` : ""}`);

  grade = (cardId: string, grade: ReviewGrade) =>
    this.json<{ cardId: string; review: ReviewState }>("POST", `/api/v1/reviews/${encodeURIComponent(cardId)}`, { grade });

  undo = (id: string, version: number) =>
    this.json<BatchResult>("POST", `/api/v1/canvases/${encodeURIComponent(id)}/batches/${version}/undo`);

  revert = (id: string, toVersion: number, ifMatch: number) =>
    this.json<BatchResult>("POST", `/api/v1/canvases/${encodeURIComponent(id)}/revert`, { toVersion }, ifMatch);

  history = (id: string, limit = 100) => this.json<HistoryEntry[]>("GET", `/api/v1/canvases/${encodeURIComponent(id)}/history?limit=${limit}`);

  search = (q: string, canvas?: string) =>
    this.json<SearchResult>("GET", `/api/v1/search?q=${encodeURIComponent(q)}${canvas ? `&canvas=${encodeURIComponent(canvas)}` : ""}&limit=30`);

  backupNow = () => this.json<{ file: string }>("POST", "/api/v1/backups");

  eventsUrl = (id: string) => `/api/v1/canvases/${encodeURIComponent(id)}/events`;

  private async json<T>(method: string, path: string, body?: unknown, ifMatch?: number): Promise<T> {
    const headers: Record<string, string> = { Accept: "application/json" };
    if (method !== "GET") headers["X-Sprawl-Request"] = "1";
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (ifMatch !== undefined) headers["If-Match"] = `"${ifMatch}"`;
    const init: RequestInit = { method, headers, credentials: "same-origin" };
    if (body !== undefined) init.body = JSON.stringify(body);
    const response = await this.fetcher(path, init);
    if (response.status === 204) return undefined as T;
    const text = await response.text();
    if (!response.ok) throw toError(response.status, text);
    return (text.length > 0 ? JSON.parse(text) : undefined) as T;
  }
}

export function toError(status: number, text: string): ApiError {
  try {
    const problem = JSON.parse(text) as { code?: string; detail?: string; title?: string; currentVersion?: number; entities?: string[] };
    return new ApiError(status, problem.code ?? `http_${status}`, problem.detail ?? problem.title ?? `Request failed (${status}).`,
      problem.currentVersion ?? null, problem.entities ?? []);
  } catch {
    return new ApiError(status, `http_${status}`, status === 401 ? "Sign in first." : `Request failed (${status}).`);
  }
}

export const api = new SprawlClient();
