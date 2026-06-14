import type { ParsedEvent } from "./utils";

export interface EventColor {
  /** Gefüllter Block-Hintergrund (Google-Style). */
  bg: string;
  /** Lesbare Vordergrundfarbe auf `bg`. */
  fg: string;
  /** Kräftiger Akzent (Rand, Marker). */
  accent: string;
}

/**
 * Gedämpfte, dark-theme-taugliche Palette im Geist von Google Calendar.
 * Gefüllte Blöcke mit heller Schrift statt der früheren monochromen Kästen.
 */
const PALETTE: EventColor[] = [
  { bg: "#37527d", fg: "#dce7fb", accent: "#7fa6e8" }, // blau
  { bg: "#4a3f7a", fg: "#e6dffb", accent: "#a593f0" }, // violett
  { bg: "#7d3f46", fg: "#fbdce0", accent: "#e98a92" }, // rot
  { bg: "#2f5e4f", fg: "#d9f3e8", accent: "#6cc6a4" }, // grün
  { bg: "#6b5630", fg: "#f7e8cb", accent: "#d9b574" }, // bernstein
  { bg: "#3d5e6b", fg: "#d6eef5", accent: "#79c2d6" }, // petrol
  { bg: "#6b3f63", fg: "#f7dcf0", accent: "#d68fc6" }, // magenta
];

/** Ganztags-Termine bekommen eine eigene, ruhige Farbe. */
const ALL_DAY_COLOR: EventColor = {
  bg: "#2c3c52",
  fg: "#cfe0f5",
  accent: "#60a5fa",
};

function hashString(value: string): number {
  let h = 0;
  for (let i = 0; i < value.length; i++) {
    h = (Math.imul(h, 31) + value.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

/**
 * Deterministische Farbe pro Termin. Gleiche Titel (z. B. Serientermine)
 * erhalten dieselbe Farbe — wie man es von Kalendern erwartet.
 */
export function eventColor(event: ParsedEvent): EventColor {
  if (event.isAllDay) return ALL_DAY_COLOR;
  const key = event.title?.trim() || event.id;
  return PALETTE[hashString(key) % PALETTE.length];
}
