import type { Card, CanvasDocument } from "../api/types";

/**
 * The client's half of Sprawl.Domain/Semantics.cs: `[[links]]` and `#tags` read from card text. Kept to what the
 * canvas needs to draw (link edges, clickable links, tag filters); backlinks, mentions and related cards come from
 * the API so there is one implementation of each analysis.
 */

const FENCED = /```[\s\S]*?(```|$)/g;
const INLINE = /`[^`\n]*`/g;
const WIKI = /\[\[([^[\]\n]{1,160})\]\]/g;
const HASHTAG = /(?<![\p{L}\p{N}_/#&=?:.])#([\p{L}\p{N}][\p{L}\p{N}_-]{0,31})/gu;

const TOKENS = new RegExp(`${WIKI.source}|${HASHTAG.source}`, "gu");

export const prose = (text: string) => text.replace(FENCED, " ").replace(INLINE, " ");

/** Same rule as Text.Normalize: case-insensitive, whitespace runs collapsed, trailing punctuation ignored. */
export const normalizeTitle = (title: string) => title.trim().toLowerCase().replace(/\s+/g, " ").replace(/[.?!:;,]+$/, "");

export function linkTargets(text: string): string[] {
  const seen = new Set<string>();
  const targets: string[] = [];
  for (const match of prose(text).matchAll(WIKI)) {
    const target = match[1]!.split("|")[0]!.trim();
    if (target && !seen.has(target.toLowerCase())) { seen.add(target.toLowerCase()); targets.push(target); }
  }
  return targets;
}

export function tagsOf(card: Pick<Card, "title" | "body">): string[] {
  const tags: string[] = [];
  for (const match of prose(`${card.title}\n${card.body}`).matchAll(HASHTAG)) {
    const tag = match[1]!.toLowerCase().replace(/-+$/, "");
    if (tag && !/^\d+$/.test(tag) && !tags.includes(tag)) tags.push(tag);
  }
  return tags.slice(0, 12);
}

/** Resolves link targets on one canvas: a card id, or a title (oldest card wins, as on the server). */
export class LinkIndex {
  private readonly byId: Map<string, Card>;
  private readonly byTitle = new Map<string, Card>();

  constructor(doc: Pick<CanvasDocument, "cards">) {
    this.byId = new Map(doc.cards.map((c) => [c.id, c]));
    const oldestFirst = [...doc.cards].sort((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id));
    for (const card of oldestFirst) {
      const key = normalizeTitle(card.title);
      if (!this.byTitle.has(key)) this.byTitle.set(key, card);
    }
  }

  resolve = (target: string): Card | undefined => this.byId.get(target.trim()) ?? this.byTitle.get(normalizeTitle(target));

  /** Every resolved text link as [from, to], each pair once, never to itself. */
  links(doc: Pick<CanvasDocument, "cards">): [string, string][] {
    const pairs: [string, string][] = [];
    for (const card of doc.cards) {
      const seen = new Set<string>();
      for (const target of linkTargets(card.body)) {
        const found = this.resolve(target);
        if (found && found.id !== card.id && !seen.has(found.id)) { seen.add(found.id); pairs.push([card.id, found.id]); }
      }
    }
    return pairs;
  }
}

/**
 * Markdown with `[[links]]` and `#tags` turned into fragment links the card renderer intercepts (`#sprawl-link/…`,
 * `#sprawl-tag/…`). Fragment URLs survive react-markdown's URL sanitising; code spans and blocks are left alone.
 */
export function linkify(markdown: string): string {
  const parts = markdown.split(/(```[\s\S]*?(?:```|$)|`[^`\n]*`)/g);
  return parts.map((part, index) => {
    if (index % 2 === 1) return part;
    // One pass over both patterns, so a tag never matches inside a link this function just wrote.
    return part.replace(TOKENS, (all, inner: string | undefined, tag: string | undefined) => {
      if (tag !== undefined) return `[#${tag}](#sprawl-tag/${encodeURIComponent(tag.toLowerCase())})`;
      const [target, alias] = (inner ?? "").split("|").map((s) => s.trim());
      return target ? `[${escapeLabel(alias || target)}](#sprawl-link/${encodeURIComponent(target)})` : all;
    });
  }).join("");
}

const escapeLabel = (label: string) => label.replace(/([\\[\]])/g, "\\$1");

/** Turn the first plain mention of `title` into a `[[link]]` (or append one): same as Semantics.LinkFirstMention. */
export function linkFirstMention(body: string, title: string): string {
  const escaped = title.trim().replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const phrase = new RegExp(`(?<![\\p{L}\\p{N}\\[])${escaped}(?![\\p{L}\\p{N}\\]])`, "iu");
  const match = phrase.exec(body);
  if (match) return `${body.slice(0, match.index)}[[${match[0]}]]${body.slice(match.index + match[0].length)}`;
  return `${body.trimEnd()}${body.length > 0 ? " " : ""}[[${title.trim()}]]`;
}
