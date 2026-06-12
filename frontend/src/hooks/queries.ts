import { useCallback } from "react";
import { endOfDay, format, startOfDay } from "date-fns";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { getCalendarRange, NotConnectedError } from "@/api/calendar";
import { getCalendarSettings } from "@/api/calendar-settings";
import type { SuggestionStatus } from "@/api/suggestions";

/** Zentrale Query-Keys — Prefixe für partielle Invalidierung. */
export const queryKeys = {
  calendarSettings: ["calendar-settings"] as const,
  calendarRangePrefix: ["calendar-range"] as const,
  calendarRange: (fromIso: string, toIso: string) =>
    ["calendar-range", fromIso, toIso] as const,
  calendarTodayPrefix: ["calendar-today"] as const,
  calendarToday: (day: string) => ["calendar-today", day] as const,
  suggestionsPrefix: ["suggestions"] as const,
  suggestions: (filter: SuggestionStatus | "all") => ["suggestions", filter] as const,
  llmSettings: ["llm-settings"] as const,
  ollamaSettings: ["ollama-settings"] as const,
  capabilities: ["capabilities"] as const,
  chatHistory: ["chat-history"] as const,
};

/** NotConnected (409) ändert sich nicht von allein — kein Retry darauf. */
function retryUnlessNotConnected(failureCount: number, error: Error): boolean {
  return !(error instanceof NotConnectedError) && failureCount < 1;
}

export function useCalendarSettingsQuery() {
  return useQuery({
    queryKey: queryKeys.calendarSettings,
    queryFn: getCalendarSettings,
  });
}

export function useCalendarRangeQuery(from: Date, to: Date, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.calendarRange(from.toISOString(), to.toISOString()),
    queryFn: () => getCalendarRange(from, to),
    enabled,
    placeholderData: keepPreviousData,
    retry: retryUnlessNotConnected,
  });
}

export function useTodayEventsQuery() {
  const day = format(new Date(), "yyyy-MM-dd");
  return useQuery({
    queryKey: queryKeys.calendarToday(day),
    queryFn: () => getCalendarRange(startOfDay(new Date()), endOfDay(new Date())),
    retry: retryUnlessNotConnected,
  });
}

/** Invalidiert Kalender-Raster + Heute-Sidebar — Ersatz für den alten reloadKey. */
export function useInvalidateCalendar() {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.calendarRangePrefix });
    void queryClient.invalidateQueries({ queryKey: queryKeys.calendarTodayPrefix });
  }, [queryClient]);
}
