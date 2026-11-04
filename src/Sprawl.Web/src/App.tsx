import { useCallback, useEffect, useState } from "react";
import { api } from "./api/client";
import type { Session } from "./api/types";
import { SignIn } from "./auth/SignIn";
import { CanvasPage } from "./canvas/CanvasPage";
import { Home } from "./home/Home";
import { parseRoute, usePath } from "./router";

export function App() {
  const [session, setSession] = useState<Session | null>(null);
  const [error, setError] = useState<string | null>(null);
  const route = parseRoute(usePath());

  const refresh = useCallback(() => {
    api.session().then(setSession, (failure: Error) => setError(failure.message));
  }, []);
  useEffect(refresh, [refresh]);

  if (error) return <p className="empty">{error}</p>;
  if (!session) return null;
  if (!session.signedIn) return <SignIn session={session} onSignedIn={refresh} />;
  return route.name === "canvas" ? <CanvasPage key={route.id} id={route.id} /> : <Home onSignOut={refresh} />;
}
