interface CalendarEvent {
  day: number; // 0-4 (Mo-Fr)
  start: number; // hour, decimal allowed
  end: number;
  label: string;
  kind: "work" | "personal" | "focus";
  conflict?: boolean;
}

interface SuggestedSlot {
  day: number;
  start: number;
  end: number;
}

interface CalendarCardProps {
  title?: string;
  subtitle?: string;
  events?: CalendarEvent[];
  suggestedSlot?: SuggestedSlot;
  showLegend?: boolean;
  density?: "compact" | "comfortable";
  onTitleClick?: () => void;
}

const HOURS = [9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
const DAYS = ["MO", "DI", "MI", "DO", "FR"];

const SAMPLE_EVENTS: CalendarEvent[] = [
  { day: 0, start: 9, end: 10.5, label: "Standup", kind: "work" },
  { day: 0, start: 14, end: 15, label: "1:1 Lina", kind: "personal" },
  { day: 1, start: 10, end: 11.5, label: "Deep Work", kind: "focus" },
  { day: 1, start: 13, end: 14, label: "Lunch · Tom", kind: "personal" },
  { day: 2, start: 9.5, end: 11, label: "Roadmap Q3", kind: "work" },
  { day: 2, start: 14, end: 15.5, label: "Design Review", kind: "work", conflict: true },
  { day: 2, start: 14.5, end: 16, label: "Investor Call", kind: "work", conflict: true },
  { day: 3, start: 10, end: 12, label: "Workshop", kind: "focus" },
  { day: 3, start: 15, end: 16, label: "Coffee · Anna", kind: "personal" },
  { day: 4, start: 9, end: 10, label: "Review", kind: "work" },
];

const KIND_COLOR: Record<CalendarEvent["kind"], string> = {
  work: "#f5f5f4",
  personal: "#f472b6",
  focus: "#60a5fa",
};

export function CalendarCard({
  title = "// CALENDAR · WEEK 21",
  subtitle = "MO 19 — FR 23 MAY",
  events = SAMPLE_EVENTS,
  suggestedSlot = { day: 4, start: 11, end: 12 },
  showLegend = true,
  density = "comfortable",
  onTitleClick,
}: CalendarCardProps) {
  const rowH = density === "compact" ? 28 : 34;
  const hourLabelW = 32;
  const dayColCount = 5;

  return (
    <div className="rounded-[4px] border border-nau-line bg-nau-bg-alt p-4 font-sans text-nau-fg">
      {/* header */}
      <div className="mb-3.5 flex items-center justify-between">
        <button
          type="button"
          onClick={onTitleClick}
          disabled={!onTitleClick}
          className={
            "font-mono text-[11px] tracking-mono text-nau-accent " +
            (onTitleClick
              ? "cursor-pointer transition-opacity hover:opacity-80"
              : "cursor-default")
          }
          aria-label={onTitleClick ? "Kalender-Seite öffnen" : undefined}
        >
          {title}
        </button>
        <span className="font-mono text-[11px] tracking-mono text-nau-fg-dim">{subtitle}</span>
      </div>

      {/* grid */}
      <div className="relative">
        {/* day header */}
        <div
          className="mb-1 grid"
          style={{
            gridTemplateColumns: `${hourLabelW}px repeat(${dayColCount}, 1fr)`,
          }}
        >
          <span />
          {DAYS.map((d, i) => (
            <span
              key={d}
              className="pl-1.5 text-left font-mono text-[10px] tracking-mono"
              style={{ color: i === 2 ? "#facc15" : "#888885" }}
            >
              {d}
              {i === 2 && " · TODAY"}
            </span>
          ))}
        </div>

        {/* hour grid */}
        <div
          className="relative grid"
          style={{
            gridTemplateColumns: `${hourLabelW}px repeat(${dayColCount}, 1fr)`,
          }}
        >
          {/* hour labels column */}
          <div>
            {HOURS.map((h) => (
              <div
                key={h}
                className="font-mono text-[9px] text-nau-fg-dim"
                style={{ height: rowH, lineHeight: `${rowH}px` }}
              >
                {String(h).padStart(2, "0")}:00
              </div>
            ))}
          </div>

          {/* day columns */}
          {DAYS.map((_d, dayIdx) => (
            <div key={dayIdx} className="relative border-l border-nau-line">
              {HOURS.map((h, hi) => (
                <div
                  key={h}
                  style={{
                    height: rowH,
                    borderBottom:
                      hi === HOURS.length - 1 ? "none" : "1px dashed rgba(255,255,255,0.04)",
                  }}
                />
              ))}

              {/* events for this day */}
              {events
                .filter((e) => e.day === dayIdx)
                .map((e, i) => {
                  const top = (e.start - HOURS[0]) * rowH;
                  const height = (e.end - e.start) * rowH;
                  const color = KIND_COLOR[e.kind];
                  return (
                    <div
                      key={`${dayIdx}-${i}`}
                      className="absolute overflow-hidden font-mono text-[9px] text-nau-fg"
                      style={{
                        top,
                        left: e.conflict ? (i % 2 === 0 ? 3 : "50%") : 3,
                        width: e.conflict ? "calc(50% - 6px)" : "calc(100% - 6px)",
                        height: height - 3,
                        background: "rgba(255,255,255,0.04)",
                        borderLeft: `2px solid ${color}`,
                        padding: "4px 6px",
                        letterSpacing: "0.02em",
                        outline: e.conflict ? "1px solid #f472b6" : "none",
                      }}
                    >
                      {e.label}
                    </div>
                  );
                })}

              {/* conflict overlay (Mittwoch) */}
              {dayIdx === 2 && (
                <div
                  className="pointer-events-none absolute left-0 w-full border border-dashed border-nau-danger"
                  style={{
                    top: (14 - HOURS[0]) * rowH - 2,
                    height: 1.5 * rowH + 4,
                  }}
                >
                  <span
                    className="absolute right-0 bg-nau-bg px-1 font-mono text-[9px] tracking-mono text-nau-danger"
                    style={{ top: -10 }}
                  >
                    ! CONFLICT
                  </span>
                </div>
              )}

              {/* suggested slot */}
              {suggestedSlot && suggestedSlot.day === dayIdx && (
                <div
                  className="absolute font-mono text-[9px] text-nau-accent"
                  style={{
                    top: (suggestedSlot.start - HOURS[0]) * rowH,
                    left: 3,
                    width: "calc(100% - 6px)",
                    height: (suggestedSlot.end - suggestedSlot.start) * rowH - 3,
                    background: "rgba(250,204,21,0.12)",
                    border: "1px solid #facc15",
                    padding: "4px 6px",
                    letterSpacing: "0.05em",
                  }}
                >
                  → SUGGESTED
                </div>
              )}
            </div>
          ))}
        </div>
      </div>

      {/* legend */}
      {showLegend && (
        <div className="mt-3.5 flex flex-wrap gap-4 border-t border-nau-line pt-3">
          {[
            { c: "#f5f5f4", label: "WORK" },
            { c: "#f472b6", label: "PERSONAL" },
            { c: "#60a5fa", label: "FOCUS" },
            { c: "#facc15", label: "SUGGESTED", border: true },
            { c: "#f472b6", label: "CONFLICT", border: true },
          ].map((it, i) => (
            <span key={i} className="inline-flex items-center gap-1.5">
              <span
                className="h-2.5 w-2.5"
                style={{
                  background: it.border ? "transparent" : it.c,
                  border: it.border ? `1px solid ${it.c}` : "none",
                }}
              />
              <span className="font-mono text-[9px] tracking-mono text-nau-fg-dim">{it.label}</span>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
