interface FocusItem {
  time: string;
  label: string;
  flag?: string;
}

interface FocusPanelProps {
  items?: FocusItem[];
}

const DEFAULT_ITEMS: FocusItem[] = [
  { time: "09:30", label: "Roadmap Q3" },
  { time: "14:00", label: "Design Review", flag: "CONFLICT" },
  { time: "14:30", label: "Investor Call", flag: "CONFLICT" },
];

export function FocusPanel({ items = DEFAULT_ITEMS }: FocusPanelProps) {
  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-3.5">
      <div className="mb-2.5 font-mono text-[10px] tracking-mono text-nau-fg-dim">
        // FOKUS_HEUTE
      </div>
      <div className="flex flex-col gap-2">
        {items.map((it, i) => (
          <div key={i} className="flex items-center gap-3 font-mono text-[11px]">
            <span className="w-10 text-nau-fg-dim">{it.time}</span>
            <span className="flex-1 text-nau-fg">{it.label}</span>
            {it.flag && (
              <span
                className="border border-nau-danger px-1.5 py-0.5 font-mono text-[9px] tracking-mono-wide text-nau-danger"
              >
                {it.flag}
              </span>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
