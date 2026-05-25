export interface Rule {
  id: number;
  text: string;
  /** Bitmask aus DayOfWeekFlags (Mo=1, Di=2, Mi=4, Do=8, Fr=16, Sa=32, So=64). */
  daysOfWeek: number;
  /** "HH:mm" oder null. */
  timeRangeStart: string | null;
  timeRangeEnd: string | null;
  hardness: "hard" | "soft";
  createdAt: string;
}

export async function getRules(): Promise<Rule[]> {
  const res = await fetch("/api/rules/");
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: "Unbekannter Fehler" }));
    throw new Error(body.error ?? `getRules failed: ${res.status}`);
  }
  return (await res.json()) as Rule[];
}
