/// <reference lib="webworker" />
import ELK from "elkjs/lib/elk.bundled.js";
import type { ElkNode } from "elkjs/lib/elk-api";

const elk = new ELK();

self.onmessage = async (event: MessageEvent<{ id: number; graph: ElkNode }>) => {
  const { id, graph } = event.data;
  try {
    self.postMessage({ id, result: await elk.layout(graph) });
  } catch (error) {
    self.postMessage({ id, error: error instanceof Error ? error.message : String(error) });
  }
};
