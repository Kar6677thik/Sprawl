import { memo, useEffect, useRef, useState } from "react";
import { currentTheme } from "./MermaidView";

type Libraries = { vega: typeof import("vega"); vegaLite: typeof import("vega-lite"); interpreter: typeof import("vega-interpreter") };
let loader: Promise<Libraries> | null = null;

/** Vega and Vega-Lite are large, so they load on first use. */
function libraries(): Promise<Libraries> {
  loader ??= Promise.all([import("vega"), import("vega-lite"), import("vega-interpreter")])
    .then(([vega, vegaLite, interpreter]) => ({ vega, vegaLite, interpreter }));
  return loader;
}

/** Colours that match the canvas, light or dark. */
function config(theme: "light" | "dark") {
  const ink = theme === "dark" ? "#d8d9dc" : "#2a2d31";
  const grid = theme === "dark" ? "#34373c" : "#e6e7ea";
  return {
    background: "transparent",
    font: "Inter Variable, Inter, system-ui, sans-serif",
    axis: { labelColor: ink, titleColor: ink, gridColor: grid, domainColor: grid, tickColor: grid, labelFontSize: 11, titleFontSize: 12 },
    legend: { labelColor: ink, titleColor: ink },
    title: { color: ink },
    view: { stroke: "transparent" },
    range: { category: ["#4a7fd6", "#e08a3c", "#3f9a63", "#c4524a", "#8a62c9", "#2f9fa8", "#b28f20", "#8a8f98"] },
  };
}

/**
 * A chart from a Vega-Lite spec. The spec is compiled to Vega and run through Vega's expression *interpreter*
 * (`ast: true`), never `new Function`, so it works under the page's CSP and no stored text becomes code. The server
 * already refused specs with URLs, so the data is exactly what is stored.
 */
export const ChartView = memo(function ChartView({ spec }: { spec: string }) {
  const host = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [theme, setTheme] = useState(currentTheme);

  useEffect(() => {
    const update = () => setTheme(currentTheme());
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    let view: { finalize: () => void } | null = null;
    let live = true;
    setError(null);
    (async () => {
      const { vega, vegaLite, interpreter } = await libraries();
      const parsed = JSON.parse(spec) as Record<string, unknown>;
      const compiled = vegaLite.compile(parsed as never, { config: config(theme) as never }).spec;
      const runtime = vega.parse(compiled, undefined, { ast: true });
      if (!live || !host.current) return;
      const created = new vega.View(runtime, { expr: interpreter.expressionInterpreter, renderer: "svg", container: host.current, hover: true });
      view = created;
      await created.runAsync();
    })().catch((failure: unknown) => { if (live) setError(failure instanceof Error ? failure.message.split("\n")[0]! : "This chart could not be drawn."); });
    return () => { live = false; view?.finalize(); };
  }, [spec, theme]);

  if (error) return <div className="mermaid-view is-error" role="img" aria-label="Chart error"><strong>Chart error</strong><span>{error}</span></div>;
  return <div className="chart-view" ref={host} role="img" aria-label="Chart" />;
});
