import { useCallback, useEffect, useState } from "react";
import { api } from "../api/client";
import type { DueQuestion, ReviewGrade } from "../api/types";
import { Body } from "./parts";

interface Props {
  canvasId: string;
  onOpen: (cardId: string) => void;
  onAsk: () => void;
  onClose: () => void;
}

const GRADES: { grade: ReviewGrade; label: string; hint: string; key: string }[] = [
  { grade: "again", label: "Again", hint: "Still open and pressing: back tomorrow", key: "1" },
  { grade: "hard", label: "Hard", hint: "Barely moved: back soon", key: "2" },
  { grade: "good", label: "Good", hint: "Thought it through: space it out", key: "3" },
  { grade: "easy", label: "Easy", hint: "Settled for now: much later", key: "4" },
];

/**
 * Spaced review of this canvas's open questions (R). Each due question is shown with its notes; grading it schedules
 * the next time it comes back. Answering stays on the canvas: open the card, or ask Claude.
 */
export function ReviewPanel({ canvasId, onOpen, onAsk, onClose }: Props) {
  const [queue, setQueue] = useState<DueQuestion[] | null>(null);
  const [done, setDone] = useState(0);
  useEffect(() => { api.reviews(canvasId).then((q) => setQueue(q.due), () => setQueue([])); }, [canvasId]);
  const current = queue?.[0];

  const grade = useCallback((value: ReviewGrade) => {
    if (!current) return;
    setQueue((q) => q?.slice(1) ?? null);
    setDone((n) => n + 1);
    api.grade(current.card.id, value).catch(() => {
      setDone((n) => n - 1);
      setQueue((q) => (q ? [current, ...q] : [current]));
    });
  }, [current]);

  // 1-4 grade the question shown; captured first so the canvas's own 1/2/3 zoom keys do not also fire.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.target instanceof Element && event.target.closest("input, textarea, select")) return;
      if (event.ctrlKey || event.metaKey || event.altKey) return;
      const match = GRADES.find((g) => g.key === event.key);
      if (match && current) { event.preventDefault(); event.stopPropagation(); grade(match.grade); }
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [current, grade]);

  return (
    <aside className="panel" aria-label="Review">
      <header className="panel-header">
        <span className="panel-title">Review{queue ? ` · ${queue.length} due` : ""}</span>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Close">×</button>
      </header>
      {!queue ? <p className="panel-empty">Loading…</p> : !current ? (
        <p className="panel-empty">
          {done > 0 ? `Done: ${done} reviewed. ` : "No open questions are due. "}
          A question comes due a day after it is written, then at growing intervals.
        </p>
      ) : (
        <div className="panel-body">
          <p className="review-where">{current.card.regionTitle ?? "Loose card"} · {current.review.reps === 0 ? "first review" : `reviewed ${current.review.reps}×`}</p>
          <h3 className="review-question">{current.card.title}</h3>
          {current.body && <div className="panel-preview"><Body markdown={current.body} /></div>}
          <div className="review-actions">
            <button type="button" className="quiet small" onClick={() => onOpen(current.card.id)}>Open card</button>
            <button type="button" className="quiet small" onClick={onAsk} title="Copy a prompt that asks Claude to research the due questions">Ask Claude</button>
          </div>
          <div className="review-grades" role="group" aria-label="How open is it still?">
            {GRADES.map((g) => (
              <button key={g.grade} type="button" title={g.hint} onClick={() => grade(g.grade)}>
                {g.label} <kbd>{g.key}</kbd>
              </button>
            ))}
          </div>
        </div>
      )}
    </aside>
  );
}
