import { useEffect, useState } from "react";
import { ChatView } from "@/components/ChatView";
import { SettingsPage } from "@/components/pages/SettingsPage";
import { CalendarPage } from "@/components/pages/CalendarPage";
import { RecommendationsPage } from "@/components/pages/RecommendationsPage";

export type AppPage = "chat" | "calendar" | "recommendations" | "settings";

export default function App() {
  const [page, setPage] = useState<AppPage>("chat");
  const [focusSuggestionId, setFocusSuggestionId] = useState<number | null>(null);

  // Deep-Link nach Push-Tap: ?suggestion=42 → direkt in Empfehlungen springen.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const raw = params.get("suggestion");
    if (raw === null) return;
    const id = Number.parseInt(raw, 10);
    if (Number.isFinite(id) && id > 0) {
      setFocusSuggestionId(id);
      setPage("recommendations");
    }
    // Param aus URL ziehen, damit Reload nicht erneut springt.
    params.delete("suggestion");
    const newSearch = params.toString();
    const newUrl =
      window.location.pathname + (newSearch ? `?${newSearch}` : "") + window.location.hash;
    window.history.replaceState({}, "", newUrl);
  }, []);

  if (page === "settings") {
    return <SettingsPage onNavigate={setPage} />;
  }
  if (page === "calendar") {
    return <CalendarPage onNavigate={setPage} />;
  }
  if (page === "recommendations") {
    return (
      <RecommendationsPage
        onNavigate={setPage}
        focusSuggestionId={focusSuggestionId}
        onFocusHandled={() => setFocusSuggestionId(null)}
      />
    );
  }
  return <ChatView onNavigate={setPage} />;
}
