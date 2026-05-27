import { useEffect, useState } from "react";
import { getPersona, resetPersona, type PersonaDto } from "@/api/settings";

interface PersonaSectionProps {
  anchor: string;
}

export function PersonaSection({ anchor }: PersonaSectionProps) {
  const [persona, setPersona] = useState<PersonaDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = async () => {
    setError(null);
    try {
      const data = await getPersona();
      setPersona(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  useEffect(() => {
    void reload();
  }, []);

  const onReset = async () => {
    if (!confirm("Persona-Memory wirklich zurücksetzen? Der Agent fängt dann wieder bei Null an.")) return;
    setBusy(true);
    setError(null);
    try {
      await resetPersona();
      await reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div id={anchor}>
      <div className="mb-6 pt-2">
        <div className="mb-4 flex items-center gap-3.5">
          <span className="font-mono text-[13px] font-bold text-nau-accent">04</span>
          <span className="h-px w-8 bg-nau-line" />
          <span className="font-mono text-[10px] tracking-mono-xwide text-nau-fg-dim">
            PERSONA-MEMORY
          </span>
        </div>
        <h2 className="m-0 mb-2 font-sans text-3xl font-normal leading-tight tracking-tight text-nau-fg">
          Was der Agent über dich gelernt hat.
        </h2>
        <p className="m-0 max-w-[640px] font-sans text-sm leading-relaxed text-nau-fg-dim">
          Der autonome Agent darf hier (und nur hier) sein eigenes Kontextfeld pflegen
          &mdash; Schreibstil, aktuelle Prioritäten, Projekte, Freizeit. Read-only im Chat-Agent.
          Hartes Limit 400 Zeichen.
        </p>
      </div>

      {error && (
        <div className="mb-4 border border-nau-danger bg-white/[0.02] px-4 py-3 font-mono text-[11px] tracking-mono text-nau-danger">
          // {error}
        </div>
      )}

      {persona === null ? (
        <div className="font-mono text-[11px] tracking-mono text-nau-fg-dim">// LADE …</div>
      ) : persona.text.length === 0 ? (
        <div className="border border-dashed border-nau-line px-4 py-6 text-center font-mono text-[11px] tracking-mono-wide text-nau-fg-dim">
          // Noch nichts gelernt — der Agent ergänzt das beim ersten relevanten Signal.
        </div>
      ) : (
        <div className="border border-nau-line bg-white/[0.02] p-4">
          <div className="mb-3 font-sans text-sm leading-relaxed text-nau-fg whitespace-pre-wrap">
            {persona.text}
          </div>
          <div className="flex items-center justify-between gap-3">
            <span className="font-mono text-[10px] tracking-mono text-nau-fg-dim">
              {persona.text.length} / {persona.maxLength} Zeichen
            </span>
            <button
              type="button"
              onClick={onReset}
              disabled={busy}
              className="min-h-10 cursor-pointer border border-nau-line bg-transparent px-3 py-2 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-danger hover:text-nau-danger disabled:opacity-50"
            >
              {busy ? "ZURÜCKSETZEN …" : "ZURÜCKSETZEN"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
