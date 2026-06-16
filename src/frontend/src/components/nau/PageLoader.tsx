/**
 * Zentraler Ladezustand für ganze Seiten-Inhalte. Header/Tab-Leiste bleiben
 * außerhalb sichtbar; der Loader füllt den Content-Bereich (flex-1).
 */
export function PageLoader({ label = "LADE" }: { label?: string }) {
  return (
    <div className="flex flex-1 items-center justify-center py-24">
      <span className="animate-pulse font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
        {`// ${label} …`}
      </span>
    </div>
  );
}
