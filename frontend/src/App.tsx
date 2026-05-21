import { useState } from "react";
import { ChatView } from "@/components/ChatView";
import { SettingsPage } from "@/components/pages/SettingsPage";
import { CalendarPage } from "@/components/pages/CalendarPage";

export type AppPage = "chat" | "calendar" | "settings";

export default function App() {
  const [page, setPage] = useState<AppPage>("chat");

  if (page === "settings") {
    return <SettingsPage onNavigate={setPage} />;
  }
  if (page === "calendar") {
    return <CalendarPage onNavigate={setPage} />;
  }
  return <ChatView onNavigate={setPage} />;
}
