import { useState } from "react";
import type { CardKind } from "../api/types";
import { KindGlyph } from "./parts";

export type ComposerMode = { kind: "card"; where: string } | { kind: "region" };

const KINDS: CardKind[] = ["idea", "note", "question", "code"];

/** Quick-add: one line, Enter to create. Tab cycles the card kind. */
export function Composer({ mode, onSubmit, onCancel }: { mode: ComposerMode; onSubmit: (title: string, kind: CardKind) => void; onCancel: () => void }) {
  const [title, setTitle] = useState("");
  const [kind, setKind] = useState<CardKind>("idea");
  return (
    <form
      className="composer"
      onSubmit={(event) => { event.preventDefault(); if (title.trim()) onSubmit(title.trim(), kind); }}
      aria-label={mode.kind === "card" ? "New card" : "New region"}
    >
      {mode.kind === "card" && (
        <button type="button" className="composer-kind" title={`Kind: ${kind} (Tab to change)`} onClick={() => setKind(KINDS[(KINDS.indexOf(kind) + 1) % KINDS.length]!)}>
          <KindGlyph kind={kind} />
        </button>
      )}
      <input
        autoFocus value={title} maxLength={mode.kind === "card" ? 120 : 80}
        placeholder={mode.kind === "card" ? `New ${kind} ${mode.where}` : "New region title"}
        onChange={(e) => setTitle(e.target.value)}
        onBlur={onCancel}
        onKeyDown={(e) => {
          if (e.key === "Escape") onCancel();
          if (e.key === "Tab" && mode.kind === "card") { e.preventDefault(); setKind(KINDS[(KINDS.indexOf(kind) + (e.shiftKey ? KINDS.length - 1 : 1)) % KINDS.length]!); }
        }}
      />
      <kbd>↵</kbd>
    </form>
  );
}
