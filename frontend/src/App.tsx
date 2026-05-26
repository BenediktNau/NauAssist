import { useState } from "react";
import { ChatView } from "@/components/ChatView";
import { SettingsPage } from "@/components/pages/SettingsPage";
import { CalendarPage } from "@/components/pages/CalendarPage";
import { RecommendationsPage } from "@/components/pages/RecommendationsPage";

export type AppPage = "chat" | "calendar" | "recommendations" | "settings";

export default function App() {
  const [page, setPage] = useState<AppPage>("chat");

  if (page === "settings") {
    return <SettingsPage onNavigate={setPage} />;
  }
  if (page === "calendar") {
    return <CalendarPage onNavigate={setPage} />;
  }
  if (page === "recommendations") {
    return <RecommendationsPage onNavigate={setPage} />;
  }
  return <ChatView onNavigate={setPage} />;
}
