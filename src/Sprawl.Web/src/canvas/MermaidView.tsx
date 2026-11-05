import { memo, useEffect, useState } from "react";

type MermaidApi = typeof import("mermaid").default;

let loader: Promise<MermaidApi> | null = null;
let configured: "light" | "dark" | null = null;
const cache = new Map<string, string>();
let serial = 0;

/** The page's effective theme: an explicit choice on <html>, else the system preference. */
export function currentTheme(): "light" | "dark" {
  const explicit = document.documentElement.getAttribute("data-theme");
  if (explicit === "light" || explicit === "dark") return explicit;
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * Mermaid is large, so it loads on first use. Card text is untrusted (much of it written by a model from web sources):
 * securityLevel "strict" sanitizes labels and disables click handlers, and HTML labels are off.
 */
async function mermaid(theme: "light" | "dark"): Promise<MermaidApi> {
  loader ??= import("mermaid").then((m) => m.default);
  const api = await loader;
  if (configured !== theme) {
    api.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: theme === "dark" ? "dark" : "neutral",
      fontFamily: "Inter Variable, Inter, system-ui, sans-serif",
      htmlLabels: false,
      flowchart: { htmlLabels: false },
    });
    configured = theme;
  }
  return api;
}

/** Render Mermaid source to an SVG string, cached by theme and source. Throws the parser's message on bad input. */
export async function renderMermaid(source: string, theme = currentTheme()): Promise<string> {
  const key = theme + "\u0000" + source;
  const hit = cache.get(key);
  if (hit) return hit;
  const api = await mermaid(theme);
  const { svg } = await api.render(`sprawl-mermaid-${++serial}`, source);
  cache.set(key, svg);
  if (cache.size > 200) cache.delete(cache.keys().next().value!);
  return svg;
}

/** A rendered Mermaid diagram that scales to fill its box. Re-renders when the theme changes. */
export const MermaidView = memo(function MermaidView({ source }: { source: string }) {
  const [theme, setTheme] = useState(currentTheme);
  const [svg, setSvg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const update = () => setTheme(currentTheme());
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", update);
    return () => { observer.disconnect(); media.removeEventListener("change", update); };
  }, []);

  useEffect(() => {
    let live = true;
    setError(null);
    renderMermaid(source, theme).then((result) => { if (live) setSvg(result); }, (failure: unknown) => {
      if (live) { setSvg(null); setError(failure instanceof Error ? failure.message.split("\n")[0]! : "This diagram could not be drawn."); }
    });
    return () => { live = false; };
  }, [source, theme]);

  if (error) return <div className="mermaid-view is-error" role="img" aria-label="Diagram error"><strong>Diagram error</strong><span>{error}</span></div>;
  if (!svg) return <div className="mermaid-view is-loading" aria-busy="true" />;
  // The SVG comes from Mermaid's strict mode, which sanitizes every label with DOMPurify.
  return <div className="mermaid-view" role="img" dangerouslySetInnerHTML={{ __html: svg }} />;
});
