import { useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { createEvent, NotConnectedError } from "@/api/calendar";

interface NewEventModalProps {
  open: boolean;
  onClose: () => void;
  /** Wird nach erfolgreichem Anlegen aufgerufen (z.B. um Kalender-Reload zu triggern). */
  onCreated: () => void;
}

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

function toLocalDateTimeInputValue(d: Date): string {
  return (
    `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` +
    `T${pad(d.getHours())}:${pad(d.getMinutes())}`
  );
}

function toDateInputValue(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Datetime-local "YYYY-MM-DDTHH:mm" als Local-Date parsen. */
function parseLocalDateTime(value: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!m) return null;
  return new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], 0, 0);
}

/** Date-only "YYYY-MM-DD" als Local-Mitternacht parsen. */
function parseLocalDate(value: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!m) return null;
  return new Date(+m[1], +m[2] - 1, +m[3], 0, 0, 0, 0);
}

function nowRoundedToQuarter(): Date {
  const d = new Date();
  d.setSeconds(0, 0);
  const minutes = Math.ceil(d.getMinutes() / 15) * 15;
  d.setMinutes(minutes);
  return d;
}

function addDays(d: Date, n: number): Date {
  const c = new Date(d);
  c.setDate(c.getDate() + n);
  return c;
}

export function NewEventModal({ open, onClose, onCreated }: NewEventModalProps) {
  const initialStart = useMemo(() => nowRoundedToQuarter(), []);
  const initialEnd = useMemo(() => {
    const e = new Date(initialStart);
    e.setHours(e.getHours() + 1);
    return e;
  }, [initialStart]);

  const [title, setTitle] = useState("");
  const [isAllDay, setIsAllDay] = useState(false);
  const [startValue, setStartValue] = useState(() => toLocalDateTimeInputValue(initialStart));
  const [endValue, setEndValue] = useState(() => toLocalDateTimeInputValue(initialEnd));
  const [startDateValue, setStartDateValue] = useState(() => toDateInputValue(initialStart));
  const [endDateValue, setEndDateValue] = useState(() => toDateInputValue(initialStart));
  const [location, setLocation] = useState("");
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Reset, wenn das Modal frisch geöffnet wird.
  useEffect(() => {
    if (!open) return;
    const s = nowRoundedToQuarter();
    const e = new Date(s);
    e.setHours(e.getHours() + 1);
    setTitle("");
    setIsAllDay(false);
    setStartValue(toLocalDateTimeInputValue(s));
    setEndValue(toLocalDateTimeInputValue(e));
    setStartDateValue(toDateInputValue(s));
    setEndDateValue(toDateInputValue(s));
    setLocation("");
    setDescription("");
    setSubmitting(false);
    setError(null);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  const trimmedTitle = title.trim();

  const { valid, validationError } = useMemo(() => {
    if (trimmedTitle.length === 0) {
      return { valid: false, validationError: null as string | null };
    }
    if (isAllDay) {
      const s = parseLocalDate(startDateValue);
      const e = parseLocalDate(endDateValue);
      if (!s || !e) return { valid: false, validationError: "Ungültiges Datum." };
      if (e.getTime() < s.getTime()) {
        return { valid: false, validationError: "Ende muss am Startdatum oder später liegen." };
      }
      return { valid: true, validationError: null };
    }
    const s = parseLocalDateTime(startValue);
    const e = parseLocalDateTime(endValue);
    if (!s || !e) return { valid: false, validationError: "Ungültige Zeitangabe." };
    if (e.getTime() <= s.getTime()) {
      return { valid: false, validationError: "Ende muss nach Start liegen." };
    }
    return { valid: true, validationError: null };
  }, [trimmedTitle, isAllDay, startValue, endValue, startDateValue, endDateValue]);

  if (!open) return null;

  const submit = async () => {
    if (!valid || submitting) return;
    setSubmitting(true);
    setError(null);

    let start: Date;
    let end: Date;
    if (isAllDay) {
      const s = parseLocalDate(startDateValue)!;
      const e = parseLocalDate(endDateValue)!;
      // Backend-Konvention: end exklusiv → User-Eingabe (inklusiv) + 1 Tag.
      start = s;
      end = addDays(e, 1);
    } else {
      start = parseLocalDateTime(startValue)!;
      end = parseLocalDateTime(endValue)!;
    }

    try {
      await createEvent({
        title: trimmedTitle,
        start,
        end,
        description: description.trim() ? description.trim() : null,
        location: location.trim() ? location.trim() : null,
        isAllDay,
      });
      onCreated();
      onClose();
    } catch (e) {
      if (e instanceof NotConnectedError) {
        setError("Google-Kalender ist nicht verbunden.");
      } else {
        setError(e instanceof Error ? e.message : "Anlegen fehlgeschlagen.");
      }
    } finally {
      setSubmitting(false);
    }
  };

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
        className="flex max-h-[90vh] w-full max-w-[520px] flex-col border border-nau-line-strong bg-nau-bg shadow-[0_8px_32px_rgba(0,0,0,0.6)] animate-nau-mech-open"
      >
        <header className="flex items-center justify-between border-b border-nau-line px-5 py-3">
          <span className="font-mono text-[11px] tracking-mono text-nau-accent">
            // NEUER TERMIN
          </span>
          <button
            type="button"
            onClick={onClose}
            className="cursor-pointer border border-nau-line bg-transparent px-2.5 py-1 font-mono text-[10px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent"
          >
            SCHLIESSEN
          </button>
        </header>

        <form
          className="flex flex-1 flex-col gap-4 overflow-y-auto px-5 py-4"
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <Field label="TITEL">
            <input
              type="text"
              autoFocus
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="z.B. 1:1 mit Lina"
              required
              className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-sans text-base text-nau-fg placeholder:text-nau-fg-dim focus:border-nau-accent focus:outline-none"
            />
          </Field>

          <label className="flex cursor-pointer items-center gap-2.5 font-mono text-[10px] tracking-mono text-nau-fg-dim">
            <input
              type="checkbox"
              checked={isAllDay}
              onChange={(e) => setIsAllDay(e.target.checked)}
              className="h-3.5 w-3.5 accent-nau-accent"
            />
            GANZTÄGIG
          </label>

          {isAllDay ? (
            <div className="grid grid-cols-2 gap-3">
              <Field label="START">
                <input
                  type="date"
                  value={startDateValue}
                  onChange={(e) => {
                    setStartDateValue(e.target.value);
                    // Ende mitziehen, wenn es vor dem neuen Start liegt
                    if (e.target.value > endDateValue) setEndDateValue(e.target.value);
                  }}
                  className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-mono text-sm text-nau-fg focus:border-nau-accent focus:outline-none"
                />
              </Field>
              <Field label="ENDE">
                <input
                  type="date"
                  value={endDateValue}
                  min={startDateValue}
                  onChange={(e) => setEndDateValue(e.target.value)}
                  className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-mono text-sm text-nau-fg focus:border-nau-accent focus:outline-none"
                />
              </Field>
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-3">
              <Field label="START">
                <input
                  type="datetime-local"
                  value={startValue}
                  onChange={(e) => {
                    setStartValue(e.target.value);
                    // Ende mitziehen, wenn es vor dem neuen Start liegt
                    if (e.target.value && e.target.value >= endValue) {
                      const s = parseLocalDateTime(e.target.value);
                      if (s) {
                        const newEnd = new Date(s);
                        newEnd.setHours(newEnd.getHours() + 1);
                        setEndValue(toLocalDateTimeInputValue(newEnd));
                      }
                    }
                  }}
                  className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-mono text-sm text-nau-fg focus:border-nau-accent focus:outline-none"
                />
              </Field>
              <Field label="ENDE">
                <input
                  type="datetime-local"
                  value={endValue}
                  onChange={(e) => setEndValue(e.target.value)}
                  className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-mono text-sm text-nau-fg focus:border-nau-accent focus:outline-none"
                />
              </Field>
            </div>
          )}

          <Field label="ORT (OPTIONAL)">
            <input
              type="text"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              placeholder="z.B. Büro, Zoom, Café"
              className="w-full border border-nau-line bg-white/[0.02] px-3 py-2 font-sans text-base text-nau-fg placeholder:text-nau-fg-dim focus:border-nau-accent focus:outline-none"
            />
          </Field>

          <Field label="BESCHREIBUNG (OPTIONAL)">
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              className="w-full resize-none border border-nau-line bg-white/[0.02] px-3 py-2 font-sans text-base text-nau-fg placeholder:text-nau-fg-dim focus:border-nau-accent focus:outline-none"
            />
          </Field>

          {(validationError || error) && (
            <div className="font-mono text-[10px] tracking-mono text-nau-danger">
              // {error ?? validationError}
            </div>
          )}

          <div className="mt-2 flex justify-end gap-2 border-t border-nau-line pt-4">
            <button
              type="button"
              onClick={onClose}
              disabled={submitting}
              className="cursor-pointer border border-nau-line bg-transparent px-4 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim hover:border-nau-accent hover:text-nau-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              ABBRECHEN
            </button>
            <button
              type="submit"
              disabled={!valid || submitting}
              className={
                "cursor-pointer border-none px-4 py-2 font-mono text-[11px] uppercase tracking-mono-wide transition-colors " +
                (valid && !submitting
                  ? "bg-nau-accent text-nau-bg hover:bg-yellow-300"
                  : "cursor-not-allowed bg-white/[0.06] text-nau-fg-dim")
              }
            >
              {submitting ? "LEGE AN…" : "ANLEGEN →"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );

  return createPortal(content, document.body);
}

interface FieldProps {
  label: string;
  children: React.ReactNode;
}

function Field({ label, children }: FieldProps) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">{label}</span>
      {children}
    </label>
  );
}
