import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// The built SPA is served by Sprawl.Api from its wwwroot (one process for UI + API).
// `npm run dev` is an optional fast-iteration server that proxies /api to the local API.
const api = "https://localhost:7543";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../Sprawl.Api/wwwroot",
    emptyOutDir: true,
    sourcemap: false,
    // Mermaid's diagram engines (and ELK in the Tidy worker) are large but load only when a rendered diagram appears.
    chunkSizeWarningLimit: 2500,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // The API only accepts unsafe requests whose Origin is its own UI origin, so the proxy presents that origin.
      "/api": { target: api, secure: false, changeOrigin: true, headers: { origin: api } },
    },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
