import { createElement } from "react";
import { ICON_NODES, type IconName } from "./icons";

export const isIcon = (name: string | null | undefined): name is IconName => !!name && Object.hasOwn(ICON_NODES, name);

/** One of Sprawl's icons (Lucide drawings, ISC), in the current text colour. Unknown names draw nothing. */
export function Icon({ name, size = 20, className }: { name: string | null | undefined; size?: number; className?: string }) {
  if (!isIcon(name)) return null;
  return (
    <svg className={className} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {ICON_NODES[name].map(([tag, attributes], i) => createElement(tag, { key: i, ...attributes }))}
    </svg>
  );
}
