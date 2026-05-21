interface ClearDividerProps {
  createdAt: string;
}

function formatTime(iso: string): string {
  const date = new Date(iso);
  return date.toLocaleTimeString("de-DE", { hour: "2-digit", minute: "2-digit" });
}

export function ClearDivider({ createdAt }: ClearDividerProps) {
  return (
    <div className="my-6 flex items-center gap-3 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
      <span className="h-px flex-1 bg-nau-line" />
      <span>NEUER KONTEXT · {formatTime(createdAt)}</span>
      <span className="h-px flex-1 bg-nau-line" />
    </div>
  );
}
