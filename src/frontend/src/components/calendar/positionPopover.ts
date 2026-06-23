/** Mindestabstand zum Viewport-Rand (px). */
export const POPOVER_MARGIN = 8;
/** Abstand zwischen Anker (Termin) und Popover (px). */
export const POPOVER_GAP = 8;

/** Anker-Rechteck in Viewport-Koordinaten (Teilmenge von DOMRect). */
export interface AnchorRect {
  top: number;
  left: number;
  right: number;
}

export interface Size {
  width: number;
  height: number;
}

export interface PopoverPosition {
  top: number;
  left: number;
  /** transform-origin für die Öffnen-Animation. */
  origin: string;
}

/**
 * Positioniert das Popover bevorzugt rechts neben dem Anker, sonst links davon,
 * und klemmt das Ergebnis auf BEIDEN Achsen hart in den sichtbaren Bereich —
 * so ragt es nie über den Bildschirmrand hinaus (Handy wie Desktop).
 *
 * Reine Funktion (kein DOM-Zugriff), damit die Math unabhängig prüfbar bleibt.
 * Voraussetzung für volle Sichtbarkeit: popover.width ≤ viewport.width − 2·MARGIN
 * (der Aufrufer begrenzt die Breite entsprechend).
 */
export function positionPopover(
  anchor: AnchorRect,
  popover: Size,
  viewport: Size,
): PopoverPosition {
  let originX: "left" | "right" = "left";
  let left = anchor.right + POPOVER_GAP;
  // Passt rechts neben dem Anker nicht → nach links neben den Anker spiegeln.
  if (left + popover.width > viewport.width - POPOVER_MARGIN) {
    left = anchor.left - popover.width - POPOVER_GAP;
    originX = "right";
  }
  // Unabhängig von der gewählten Seite hart in [MARGIN, vw − width − MARGIN] klemmen,
  // damit weder die linke noch die rechte Kante über den Rand läuft.
  const maxLeft = Math.max(POPOVER_MARGIN, viewport.width - popover.width - POPOVER_MARGIN);
  left = Math.min(Math.max(left, POPOVER_MARGIN), maxLeft);

  // Vertikal am Anker-Top ausrichten, dann ebenfalls in den Viewport klemmen.
  const maxTop = Math.max(POPOVER_MARGIN, viewport.height - popover.height - POPOVER_MARGIN);
  const top = Math.min(Math.max(anchor.top, POPOVER_MARGIN), maxTop);

  return { top, left, origin: `top ${originX}` };
}
