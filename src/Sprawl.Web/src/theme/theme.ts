export type ThemeChoice = "system" | "light" | "dark";

const KEY = "sprawl.theme";

export function readTheme(): ThemeChoice {
  const stored = localStorage.getItem(KEY);
  return stored === "light" || stored === "dark" ? stored : "system";
}

/** `data-theme` on <html> overrides the system preference; "system" removes it so the media query decides. */
export function applyTheme(choice: ThemeChoice) {
  if (choice === "system") {
    document.documentElement.removeAttribute("data-theme");
    localStorage.removeItem(KEY);
  } else {
    document.documentElement.setAttribute("data-theme", choice);
    localStorage.setItem(KEY, choice);
  }
}

export function nextTheme(choice: ThemeChoice): ThemeChoice {
  return choice === "system" ? "light" : choice === "light" ? "dark" : "system";
}
