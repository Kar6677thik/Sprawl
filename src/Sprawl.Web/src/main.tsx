import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@fontsource-variable/inter";
import "@xyflow/react/dist/base.css";
import "./theme/tokens.css";
import "./theme/app.css";
import { App } from "./App";
import { applyTheme, readTheme } from "./theme/theme";

applyTheme(readTheme());

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
