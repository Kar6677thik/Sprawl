/**
 * Subsequence fuzzy match for the command palette. Every query character must appear in order. The score rewards
 * matches at word starts and consecutive runs, and penalizes gaps, so "le" ranks "Leader election" above "ballet".
 * Returns null when there is no match.
 */
export function score(query: string, text: string): number | null {
  const q = query.trim().toLowerCase();
  if (q.length === 0) return 0;
  const t = text.toLowerCase();
  let total = 0;
  let from = 0;
  let previous = -2;
  for (const char of q) {
    if (char === " ") continue;
    const index = t.indexOf(char, from);
    if (index < 0) return null;
    const wordStart = index === 0 || /[\s\-_/.:(]/.test(t[index - 1]!);
    total += 1 + (wordStart ? 8 : 0) + (index === previous + 1 ? 5 : 0) - Math.min(index - from, 10) * 0.2;
    previous = index;
    from = index + 1;
  }
  if (t.startsWith(q)) total += 10;
  return total - t.length * 0.01;
}

export interface Ranked<T> { item: T; score: number }

export function rank<T>(query: string, items: T[], text: (item: T) => string, limit = 50): T[] {
  const ranked: Ranked<T>[] = [];
  for (const item of items) {
    const value = score(query, text(item));
    if (value !== null) ranked.push({ item, score: value });
  }
  return ranked.sort((a, b) => b.score - a.score).slice(0, limit).map((entry) => entry.item);
}
