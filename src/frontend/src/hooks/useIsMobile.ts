import { useEffect, useState } from "react";

/** Unterhalb des Tailwind-`lg`-Breakpoints (1024px) gilt als Mobile. */
const MOBILE_QUERY = "(max-width: 1023px)";

/** Synchroner Einzel-Check (z. B. für State-Initialisierung). */
export function isMobileViewport(): boolean {
  return typeof window !== "undefined" && window.matchMedia(MOBILE_QUERY).matches;
}

/**
 * Reagiert live auf Viewport-Wechsel (Rotation, Resize, DevTools).
 * SSR-sicher: initialer Wert wird nur im Browser aus `matchMedia` gelesen.
 */
export function useIsMobile(): boolean {
  const [isMobile, setIsMobile] = useState(() =>
    typeof window !== "undefined"
      ? window.matchMedia(MOBILE_QUERY).matches
      : false,
  );

  useEffect(() => {
    const mql = window.matchMedia(MOBILE_QUERY);
    const onChange = () => setIsMobile(mql.matches);
    onChange();
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, []);

  return isMobile;
}
