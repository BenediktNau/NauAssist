import { useCallback, useEffect, useRef, useState } from "react";
import { ChatView } from "@/components/ChatView";
import { SettingsPage } from "@/components/pages/SettingsPage";
import { CalendarPage } from "@/components/pages/CalendarPage";
import { RecommendationsPage } from "@/components/pages/RecommendationsPage";

export type AppPage = "chat" | "calendar" | "recommendations" | "settings";

// Chat ist die Heimat-Seite: Der Zurück-Knopf führt aus jeder anderen Seite zuerst
// hierhin zurück; erst von Chat aus verlässt man die App (bzw. schließt die PWA).
const HOME_PAGE: AppPage = "chat";

interface NavState {
  nauPage?: AppPage;
}

/** Deep-Link nach Push-Tap auswerten: ?suggestion=42 → direkt in die Empfehlungen. */
function readDeepLink(): { page: AppPage; focusId: number | null } {
  const raw = new URLSearchParams(window.location.search).get("suggestion");
  if (raw !== null) {
    const id = Number.parseInt(raw, 10);
    if (Number.isFinite(id) && id > 0) {
      return { page: "recommendations", focusId: id };
    }
  }
  return { page: HOME_PAGE, focusId: null };
}

export default function App() {
  const [page, setPage] = useState<AppPage>(() => readDeepLink().page);
  const [focusSuggestionId, setFocusSuggestionId] = useState<number | null>(
    () => readDeepLink().focusId,
  );

  // Spiegelt die aktuelle Seite synchron — damit `navigate` ohne Seiteneffekt im
  // State-Updater entscheiden kann, ob ein neuer History-Eintrag nötig ist.
  const pageRef = useRef<AppPage>(page);

  // Einmalig: URL säubern, Basis-History-Eintrag (aktuelle Seite) verankern und
  // Browser-Zurück/Vor an die In-App-Navigation koppeln.
  useEffect(() => {
    // URL bleibt überall "/", gesteuert wird nur über den History-State. Den
    // Deep-Link-Param entfernen, damit ein Reload nicht erneut dorthin springt.
    const params = new URLSearchParams(window.location.search);
    params.delete("suggestion");
    const search = params.toString();
    const cleanUrl =
      window.location.pathname + (search ? `?${search}` : "") + window.location.hash;
    const baseState: NavState = { nauPage: pageRef.current };
    window.history.replaceState(baseState, "", cleanUrl);

    const onPopState = (event: PopStateEvent) => {
      const next = (event.state as NavState | null)?.nauPage ?? HOME_PAGE;
      pageRef.current = next;
      setPage(next);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const navigate = useCallback((next: AppPage) => {
    if (pageRef.current === next) return;
    pageRef.current = next;
    // Neuer History-Eintrag bei gleichbleibender URL ("/") — nur der State trägt
    // die Seite. Browser-Zurück löst dann `popstate` aus und blättert zur
    // vorherigen Seite (letztlich Chat).
    const nextState: NavState = { nauPage: next };
    window.history.pushState(nextState, "", window.location.href);
    setPage(next);
  }, []);

  return (
    <div key={page} className="h-full motion-safe:animate-page-in">
      {page === "settings" ? (
        <SettingsPage onNavigate={navigate} />
      ) : page === "calendar" ? (
        <CalendarPage onNavigate={navigate} />
      ) : page === "recommendations" ? (
        <RecommendationsPage
          onNavigate={navigate}
          focusSuggestionId={focusSuggestionId}
          onFocusHandled={() => setFocusSuggestionId(null)}
        />
      ) : (
        <ChatView onNavigate={navigate} />
      )}
    </div>
  );
}
