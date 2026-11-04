import { useSyncExternalStore } from "react";

/** Two routes: "/" (canvas list) and "/c/<id>" (a canvas). The API's SPA fallback serves index.html for both. */
export type Route = { name: "home" } | { name: "canvas"; id: string };

export function parseRoute(path: string): Route {
  const match = /^\/c\/(cv_[0-9a-z]{8})\/?$/.exec(path);
  return match ? { name: "canvas", id: match[1]! } : { name: "home" };
}

const listeners = new Set<() => void>();

export function navigate(path: string) {
  if (path !== location.pathname) history.pushState(null, "", path);
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  window.addEventListener("popstate", listener);
  return () => {
    listeners.delete(listener);
    window.removeEventListener("popstate", listener);
  };
}

export function usePath() {
  return useSyncExternalStore(subscribe, () => location.pathname);
}
