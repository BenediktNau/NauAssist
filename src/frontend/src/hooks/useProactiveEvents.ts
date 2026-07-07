import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { fetchEventSource } from "@microsoft/fetch-event-source";
import { queryKeys } from "@/hooks/queries";

/**
 * Lauscht auf den server-initiierten Event-Stream (/api/events) und hält die
 * betroffenen Queries frisch: proaktive Chat-Nachrichten und feuernde Watch-Jobs
 * erscheinen live in der offenen PWA, ohne Polling.
 */
export function useProactiveEvents() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const ctrl = new AbortController();

    void fetchEventSource("/api/events", {
      signal: ctrl.signal,
      // Auch im Hintergrund-Tab verbunden bleiben — ergänzt Web-Push, ersetzt ihn nicht.
      openWhenHidden: true,
      onmessage(ev) {
        if (ev.event === "chat_message") {
          void queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
        } else if (ev.event === "watch_job_fired") {
          void queryClient.invalidateQueries({ queryKey: queryKeys.watchJobs });
          void queryClient.invalidateQueries({ queryKey: queryKeys.chatHistory });
        }
      },
      onerror() {
        // undefined zurückgeben ⇒ eingebautes Retry übernimmt. Kein Backoff: die Library
        // reconnected standardmäßig in einem festen ~1s-Intervall (nur ein Server-seitiges
        // "retry:"-Feld im Event-Stream würde das ändern).
      },
    });

    return () => ctrl.abort();
  }, [queryClient]);
}
