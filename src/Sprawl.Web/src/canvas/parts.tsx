import { createContext, memo, useContext, type ReactNode } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { CardKind } from "../api/types";
import { linkify } from "./semantics";

/** What a `[[link]]` or `#tag` in a card body does when clicked; `exists` styles links that name no card yet. */
export interface LinkActions {
  open: (target: string) => void;
  tag: (tag: string) => void;
  exists: (target: string) => boolean;
}

export const LinkContext = createContext<LinkActions | null>(null);

const GLYPHS: Record<CardKind, ReactNode> = {
  idea: <path d="M6 1.5a3.5 3.5 0 0 0-2 6.4V9.5h4V7.9a3.5 3.5 0 0 0-2-6.4ZM4.5 11h3" />,
  note: <path d="M2.5 3h7M2.5 6h7M2.5 9h4.5" />,
  question: <path d="M4.2 4.2a1.9 1.9 0 1 1 2.6 1.7c-.5.2-.8.6-.8 1.1v.5M6 9.8v.2" />,
  link: <path d="M5 7 7 5M4.3 5.2 3.2 6.3a1.8 1.8 0 0 0 2.5 2.5l1.1-1.1M7.7 6.8l1.1-1.1a1.8 1.8 0 0 0-2.5-2.5L5.2 4.3" />,
  code: <path d="M4.3 3.5 2 6l2.3 2.5M7.7 3.5 10 6 7.7 8.5" />,
  mermaid: <path d="M1.5 2h3v2.5h-3zM7.5 7.5h3V10h-3zM3 4.5v2.5h6V7.5" />,
};

/** A 12 px glyph per card kind. Kinds are told apart by shape, never by colour. */
export function KindGlyph({ kind }: { kind: CardKind }) {
  return (
    <svg className="kind-glyph" viewBox="0 0 12 12" width="12" height="12" aria-label={kind} role="img">
      <g fill="none" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round">{GLYPHS[kind]}</g>
    </svg>
  );
}

/**
 * Card bodies are untrusted text (much of it written by Claude from web sources): no raw HTML, no images (the CSP
 * would block remote ones anyway), and links open in a new tab without a referrer.
 */
export const Body = memo(function Body({ markdown }: { markdown: string }) {
  return (
    <Markdown
      remarkPlugins={[remarkGfm]}
      disallowedElements={["img"]}
      unwrapDisallowed
      components={{ a: ({ href, children }) => <BodyLink href={href}>{children}</BodyLink> }}
    >
      {linkify(markdown)}
    </Markdown>
  );
});

function BodyLink({ href, children }: { href: string | undefined; children: ReactNode }) {
  const actions = useContext(LinkContext);
  const stop = (event: { stopPropagation: () => void }) => event.stopPropagation();
  const internal = href?.match(/^#sprawl-(link|tag)\/(.*)$/);
  if (internal) {
    const value = decodeURIComponent(internal[2]!);
    const isTag = internal[1] === "tag";
    const missing = !isTag && actions !== null && !actions.exists(value);
    return (
      <button
        type="button"
        className={isTag ? "tag-chip" : `wikilink${missing ? " is-missing" : ""}`}
        title={isTag ? `Cards tagged #${value}` : missing ? `No card called "${value}" yet` : `Open "${value}"`}
        onPointerDown={stop}
        onDoubleClick={stop}
        onClick={(event) => { event.stopPropagation(); if (isTag) actions?.tag(value); else actions?.open(value); }}
      >
        {children}
      </button>
    );
  }
  return (
    <a href={href} target="_blank" rel="noopener noreferrer nofollow" onPointerDown={stop}>
      {children}
    </a>
  );
}
