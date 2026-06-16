import { useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { format } from "date-fns";
import { de } from "date-fns/locale";
import { getRules, type Rule } from "@/api/rules";

interface RulesModalProps {
  open: boolean;
  onClose: () => void;
}

const ALL_DAYS = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"] as const;

// DayOfWeekFlags-Presets aus Backend/Features/Rules/Rule.cs
const PRESET_WEEKDAYS = 1 | 2 | 4 | 8 | 16; // 31
const PRESET_WEEKEND = 32 | 64; // 96
const PRESET_ALL = PRESET_WEEKDAYS | PRESET_WEEKEND; // 127

export function RulesModal({ open, onClose }: RulesModalProps) {
  const [rules, setRules] = useState<Rule[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    setRules(null);
    getRules()
      .then((data) => {
        if (!cancelled) setRules(data);
      })
      .catch((e: unknown) => {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : "Regeln laden fehlgeschlagen.");
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  if (!open) return null;

  const content = (
    <div
      className="fixed inset-0 z-[90] flex items-center justify-center bg-black/60 p-4"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="flex max-h-[85vh] w-full max-w-[560px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // REGELN
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-5 py-4">
          {loading && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              // LADE …
            </div>
          )}
          {error && (
            <div className="font-mono text-[10px] tracking-mono text-nau-danger">
              // {error}
            </div>
          )}
          {!loading && !error && rules && rules.length === 0 && (
            <div className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              // NOCH KEINE REGELN ANGELEGT
            </div>
          )}
          {!loading && !error && rules && rules.length > 0 && (
            <ul className="flex flex-col gap-3">
              {rules.map((r) => (
                <li key={r.id}>
                  <RuleRow rule={r} />
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface RuleRowProps {
  rule: Rule;
}

function RuleRow({ rule }: RuleRowProps) {
  const hardness = rule.hardness === "hard" ? "HART" : "WEICH";
  const timeRange =
    rule.timeRangeStart && rule.timeRangeEnd
      ? `${rule.timeRangeStart}–${rule.timeRangeEnd}`
      : null;
  const created = format(new Date(rule.createdAt), "d. MMM yyyy", { locale: de });

  return (
    <div className="border-l-2 border-nau-line bg-white/[0.02] px-4 py-3">
      <div className="font-sans text-[14px] leading-snug text-nau-fg">{rule.text}</div>
      <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 font-mono text-[10px] tracking-mono text-nau-fg-dim">
        <DaysOfWeekBadge mask={rule.daysOfWeek} />
        {timeRange && <span>{timeRange}</span>}
        <span
          className={
            rule.hardness === "hard"
              ? "border border-nau-danger/60 px-1.5 py-0.5 text-nau-danger"
              : "border border-nau-line px-1.5 py-0.5 text-nau-fg-dim"
          }
        >
          {hardness}
        </span>
        <span className="ml-auto">SEIT {created.toUpperCase()}</span>
      </div>
    </div>
  );
}

interface DaysOfWeekBadgeProps {
  mask: number;
}

function DaysOfWeekBadge({ mask }: DaysOfWeekBadgeProps) {
  if (mask === PRESET_ALL) return <span>ALLE TAGE</span>;
  if (mask === PRESET_WEEKDAYS) return <span>WERKTAGS</span>;
  if (mask === PRESET_WEEKEND) return <span>WOCHENENDE</span>;

  return (
    <span className="flex gap-1">
      {ALL_DAYS.map((label, i) => {
        const active = (mask & (1 << i)) !== 0;
        return (
          <span
            key={label}
            className={active ? "text-nau-fg" : "text-nau-fg-dim/40"}
          >
            {label}
          </span>
        );
      })}
    </span>
  );
}
