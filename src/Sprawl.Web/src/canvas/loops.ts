import type { CanvasDocument } from "../api/types";

/** A feedback loop in a causal-loop diagram. Mirrors src/Sprawl.Domain/CausalLoops.cs: keep the two in step. */
export interface Loop {
  name: string;
  polarity: "R" | "B" | null;
  members: string[];
  connections: string[];
  delayed: boolean;
}

/** A loop's badge, relative to its region: the middle of its members, and which way the loop turns. */
export interface LoopBadge extends Loop {
  x: number;
  y: number;
  clockwise: boolean;
}

const MAX_LOOPS = 24;
const MAX_LENGTH = 12;
const DIRECTIONAL = new Set(["leads-to", "depends-on", "example-of"]);

/** +1 for "+" (more causes more), −1 for "−" or "-" (more causes less), 0 when the label carries no sign. */
export function sign(label: string | null | undefined): number {
  const text = (label ?? "").trim();
  if (!text) return 0;
  if (text[0] === "+") return 1;
  if (text[0] === "-" || text[0] === "−" || text[0] === "–") return -1;
  if (text.toLowerCase() === "s") return 1;
  if (text.toLowerCase() === "o") return -1;
  return 0;
}

export const isDelay = (label: string | null | undefined) => !!label && (label.includes("||") || label.toLowerCase().includes("delay"));

type Doc = Pick<CanvasDocument, "cards" | "regions" | "connections">;

const byCreation = (a: { createdAt: string; id: string }, b: { createdAt: string; id: string }) =>
  a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : a.id < b.id ? -1 : a.id > b.id ? 1 : 0;

/** Elementary cycles among a region's direct members, longest first, named R1, B1, L1 (unsigned) as the server names them. */
export function findLoops(doc: Doc, regionId: string): Loop[] {
  const members = [...doc.cards.filter((c) => c.regionId === regionId), ...doc.regions.filter((r) => r.parentId === regionId)]
    .sort(byCreation).map((m) => m.id);
  const index = new Map(members.map((id, i) => [id, i]));
  const outgoing: { to: number; id: string; label: string | null }[][] = members.map(() => []);
  for (const c of [...doc.connections].sort(byCreation)) {
    const a = index.get(c.sourceId), b = index.get(c.targetId);
    if (a === undefined || b === undefined || a === b) continue;
    const ends = c.ends ?? (DIRECTIONAL.has(c.kind) ? "end" : "none");
    if (ends === "end" || ends === "both" || ends === "none") outgoing[a]!.push({ to: b, id: c.id, label: c.label });
    if (ends === "start" || ends === "both") outgoing[b]!.push({ to: a, id: c.id, label: c.label });
  }

  const found: { nodes: number[]; via: { id: string; label: string | null }[] }[] = [];
  const path: number[] = [];
  const via: { id: string; label: string | null }[] = [];
  const onPath = members.map(() => false);
  let budget = 200_000;
  const walk = (start: number, at: number) => {
    for (const edge of outgoing[at]!) {
      if (found.length >= MAX_LOOPS * 4 || --budget < 0) return;
      if (edge.to === start) { found.push({ nodes: [...path], via: [...via, edge] }); continue; }
      if (edge.to < start || onPath[edge.to] || path.length >= MAX_LENGTH) continue;
      path.push(edge.to); via.push(edge); onPath[edge.to] = true;
      walk(start, edge.to);
      path.pop(); via.pop(); onPath[edge.to] = false;
    }
  };
  for (let start = 0; start < members.length; start++) {
    path.length = 0; via.length = 0; path.push(start); onPath[start] = true;
    walk(start, start);
    onPath[start] = false;
  }

  const key = (nodes: number[]) => nodes.map((n) => String(n).padStart(4, "0")).join(",");
  found.sort((a, b) => b.nodes.length - a.nodes.length || (key(a.nodes) < key(b.nodes) ? -1 : key(a.nodes) > key(b.nodes) ? 1 : 0));
  const counters = { R: 0, B: 0, L: 0 };
  return found.slice(0, MAX_LOOPS).map(({ nodes, via: links }) => {
    const signs = links.map((l) => sign(l.label));
    const polarity = signs.includes(0) ? null : signs.filter((s) => s < 0).length % 2 === 0 ? "R" : "B";
    const letter = polarity ?? "L";
    counters[letter]++;
    return { name: `${letter}${counters[letter]}`, polarity, members: nodes.map((n) => members[n]!), connections: links.map((l) => l.id), delayed: links.some((l) => isDelay(l.label)) };
  });
}

/** Badges for every loops region: at the middle of each loop's members, turning the way the loop runs. */
export function loopBadges(doc: Doc): Map<string, LoopBadge[]> {
  const result = new Map<string, LoopBadge[]>();
  for (const region of doc.regions.filter((r) => r.layout === "loops")) {
    const boxes = new Map<string, { x: number; y: number }>();
    for (const c of doc.cards) if (c.regionId === region.id) boxes.set(c.id, { x: c.x + c.w / 2, y: c.y + c.h / 2 });
    for (const r of doc.regions) if (r.parentId === region.id) boxes.set(r.id, { x: r.x + r.w / 2, y: r.y + r.h / 2 });
    const badges: LoopBadge[] = [];
    const all = [...boxes.values()];
    const middle = { x: all.reduce((s, p) => s + p.x, 0) / Math.max(1, all.length), y: all.reduce((s, p) => s + p.y, 0) / Math.max(1, all.length) };
    for (const loop of findLoops(doc, region.id)) {
      const points = loop.members.map((id) => boxes.get(id)).filter((p): p is { x: number; y: number } => !!p);
      if (points.length < 2) continue;
      let area = 0;
      points.forEach((p, i) => { const q = points[(i + 1) % points.length]!; area += p.x * q.y - q.x * p.y; });
      let x = points.reduce((s, p) => s + p.x, 0) / points.length, y = points.reduce((s, p) => s + p.y, 0) / points.length;
      // A two-variable loop's middle lies between its two links: move its badge over halfway toward the circle's centre (as the server does).
      if (points.length === 2) { x += (middle.x - x) * 0.55; y += (middle.y - y) * 0.55; }
      badges.push({ ...loop, x, y, clockwise: area >= 0 });
    }
    result.set(region.id, badges);
  }
  return result;
}
