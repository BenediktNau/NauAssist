import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import { getCapabilities } from "@/api/capabilities";
import { AuthContext, type AuthState } from "@/lib/authContext";

// BFF-Auth: das Frontend kennt kein Token, nur das HttpOnly-Session-Cookie.
// Login = Redirect auf den Backend-Endpoint, der den Keycloak-Flow anstößt.

interface MeDto {
  isAuthenticated: boolean;
  sub: string | null;
  username: string | null;
  email: string | null;
}

function redirectToLogin(loginUrl: string): void {
  const returnUrl = window.location.pathname + window.location.search;
  // `replace` statt `assign`: der Login ist eine Vollbild-Weiterleitung, kein Ziel,
  // zu dem man zurückblättern möchte. So bleibt die aktuelle Seite nicht als
  // toter History-Eintrag (u. a. /signin-oidc) hinter der App liegen.
  window.location.replace(`${loginUrl}?returnUrl=${encodeURIComponent(returnUrl)}`);
}

/**
 * Läuft die Session mid-use ab, antwortet das Backend mit 401 — dann direkt
 * zurück in den Login (Keycloak-SSO loggt i.d.R. transparent wieder ein).
 * Global als fetch-Wrapper, damit kein API-Modul einzeln 401 behandeln muss.
 */
function installSessionExpiryRedirect(loginUrl: string): void {
  const originalFetch = window.fetch.bind(window);
  window.fetch = async (input, init) => {
    const res = await originalFetch(input, init);
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    if (res.status === 401 && url.includes("/api/")) {
      redirectToLogin(loginUrl);
    }
    return res;
  };
}

interface AuthGateProps {
  children: ReactNode;
}

/**
 * Klärt vor dem App-Start den Login-Status: Auth aus → App wie bisher, keinerlei
 * Login-UI. Auth an & keine Session → Redirect zu Keycloak (via /auth/login).
 */
export function AuthGate({ children }: AuthGateProps) {
  const [state, setState] = useState<AuthState | null>(null);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      let enabled = false;
      let loginUrl = "/auth/login";
      try {
        const caps = await getCapabilities();
        enabled = caps.auth.enabled;
        loginUrl = caps.auth.loginUrl;
      } catch {
        // Capabilities nicht erreichbar → wie "Auth aus" starten, die API-Calls
        // der App melden Folgefehler ohnehin sichtbar.
      }

      if (!enabled) {
        if (!cancelled) {
          setState({ enabled: false, username: null, email: null, logout: async () => {} });
        }
        return;
      }

      const meRes = await fetch("/auth/me");
      const me = (await meRes.json()) as MeDto;
      if (!me.isAuthenticated) {
        redirectToLogin(loginUrl);
        return; // Redirect läuft — nichts rendern.
      }

      installSessionExpiryRedirect(loginUrl);
      if (!cancelled) {
        setState({
          enabled: true,
          username: me.username,
          email: me.email,
          logout: async () => {
            await fetch("/auth/logout", { method: "POST" });
            window.location.assign("/");
          },
        });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  if (state === null) {
    return null; // kurzer Moment beim Boot bzw. während des Login-Redirects
  }

  return <AuthContext.Provider value={state}>{children}</AuthContext.Provider>;
}
