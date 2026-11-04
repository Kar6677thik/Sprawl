import { useState } from "react";
import { applyTheme, nextTheme, readTheme, type ThemeChoice } from "./theme";

const LABEL: Record<ThemeChoice, string> = { system: "Theme: system", light: "Theme: light", dark: "Theme: dark" };

export function ThemeButton() {
  const [choice, setChoice] = useState<ThemeChoice>(readTheme);
  return (
    <button
      type="button"
      className="icon-button"
      title={LABEL[choice]}
      aria-label={LABEL[choice]}
      onClick={() => {
        const next = nextTheme(choice);
        applyTheme(next);
        setChoice(next);
      }}
    >
      <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">
        <circle cx="8" cy="8" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.3" />
        {choice !== "light" && <path d="M8 2.5a5.5 5.5 0 0 1 0 11Z" fill="currentColor" />}
        {choice === "system" && <path d="M8 2.5v11" stroke="currentColor" strokeWidth="1.3" />}
      </svg>
    </button>
  );
}
