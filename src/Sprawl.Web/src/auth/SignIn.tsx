import { useState, type FormEvent } from "react";
import { Mark } from "../theme/Mark";
import { api } from "../api/client";
import type { Session } from "../api/types";

export function SignIn({ session, onSignedIn }: { session: Session; onSignedIn: () => void }) {
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onSignedIn();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    void run(() => api.signIn(password, session.secondFactor ? code : undefined));
  };

  return (
    <main className="sign-in">
      <h1 className="wordmark"><Mark />Sprawl</h1>
      {session.passwordSignIn && (
        <form onSubmit={submit} className="sign-in-form">
          <label>
            <span>Password</span>
            <input type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus required />
          </label>
          {session.secondFactor === "totp" && (
            <label>
              <span>Authenticator code</span>
              <input inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} required />
            </label>
          )}
          <button type="submit" className="primary" disabled={busy}>Sign in</button>
        </form>
      )}
      {session.devSignIn && (
        <button type="button" className={session.passwordSignIn ? "quiet" : "primary"} disabled={busy} onClick={() => void run(api.devSignIn)}>
          Development sign-in
        </button>
      )}
      {!session.passwordSignIn && !session.devSignIn && <p className="empty">Sign-in is not configured on this server.</p>}
      {error && <p className="error" role="alert">{error}</p>}
    </main>
  );
}
