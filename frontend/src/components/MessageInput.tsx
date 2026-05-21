import { useState, type KeyboardEvent } from "react";

interface MessageInputProps {
  onSend: (text: string) => void;
  disabled: boolean;
}

const SLASH_HINTS = ["/termin", "/verschieben", "/woche", "/frei"];

export function MessageInput({ onSend, disabled }: MessageInputProps) {
  const [value, setValue] = useState("");

  const submit = () => {
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setValue("");
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  const hasContent = value.trim().length > 0;

  return (
    <div className="relative">
      <div className="mb-2.5 flex flex-wrap gap-2">
        {SLASH_HINTS.map((cmd) => (
          <button
            type="button"
            key={cmd}
            onClick={() => setValue((v) => (v ? v : `${cmd} `))}
            disabled={disabled}
            className="cursor-pointer border border-nau-line px-2.5 py-1 font-mono text-[10px] tracking-mono text-nau-fg-dim transition-colors hover:border-nau-accent hover:text-nau-accent disabled:cursor-not-allowed disabled:opacity-50"
          >
            {cmd}
          </button>
        ))}
      </div>

      <div className="flex items-center gap-3 border border-nau-line bg-white/[0.02] px-4 py-2">
        <textarea
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={onKeyDown}
          rows={1}
          disabled={disabled}
          placeholder="Sag mir, was du heute planen willst…"
          className="min-h-[28px] flex-1 resize-none bg-transparent py-1 font-sans text-base leading-snug text-nau-fg placeholder:text-nau-fg-dim focus:outline-none disabled:opacity-50"
        />
        <button
          type="button"
          onClick={submit}
          disabled={disabled || !hasContent}
          className={[
            "shrink-0 cursor-pointer border-none px-4 py-2 font-mono text-[11px] uppercase tracking-mono-wide transition-colors",
            hasContent && !disabled
              ? "bg-nau-accent text-nau-bg hover:bg-yellow-300"
              : "bg-white/[0.06] text-nau-fg-dim",
            disabled ? "cursor-not-allowed opacity-50" : "",
          ].join(" ")}
        >
          SENDEN →
        </button>
      </div>
      <div className="mt-2 font-mono text-[10px] tracking-mono text-nau-fg-dim">
        ↵ SENDEN · ⇧↵ ZEILENUMBRUCH · / COMMANDS
      </div>
    </div>
  );
}
