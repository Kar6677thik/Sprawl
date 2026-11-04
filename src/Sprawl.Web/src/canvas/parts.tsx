import { memo, type ReactNode } from "react";
import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { CardKind } from "../api/types";

const GLYPHS: Record<CardKind, ReactNode> = {
  idea: <path d="M6 1.5a3.5 3.5 0 0 0-2 6.4V9.5h4V7.9a3.5 3.5 0 0 0-2-6.4ZM4.5 11h3" />,
  note: <path d="M2.5 3h7M2.5 6h7M2.5 9h4.5" />,
  question: <path d="M4.2 4.2a1.9 1.9 0 1 1 2.6 1.7c-.5.2-.8.6-.8 1.1v.5M6 9.8v.2" />,
  link: <path d="M5 7 7 5M4.3 5.2 3.2 6.3a1.8 1.8 0 0 0 2.5 2.5l1.1-1.1M7.7 6.8l1.1-1.1a1.8 1.8 0 0 0-2.5-2.5L5.2 4.3" />,
  code: <path d="M4.3 3.5 2 6l2.3 2.5M7.7 3.5 10 6 7.7 8.5" />,
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
      components={{
        a: ({ href, children }) => (
          <a href={href} target="_blank" rel="noopener noreferrer nofollow" onPointerDown={(event) => event.stopPropagation()}>
            {children}
          </a>
        ),
      }}
    >
      {markdown}
    </Markdown>
  );
});
