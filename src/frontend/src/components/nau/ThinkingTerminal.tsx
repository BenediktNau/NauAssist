import { useEffect, useState } from "react";

interface ThinkingTerminalProps {
  label?: string;
}

export function ThinkingTerminal({ label = "DENKE NACH" }: ThinkingTerminalProps) {
  const [tick, setTick] = useState(0);

  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 220);
    return () => clearInterval(id);
  }, []);

  const barLen = 20;
  const filled = tick % (barLen + 1);
  const bar = "█".repeat(filled) + "░".repeat(barLen - filled);

  return (
    <div className="mb-6">
      <div className="mb-2 flex items-center gap-2.5">
        <span
          className="nau-dot pulse h-1.5 w-1.5"
          style={{ background: "#facc15", boxShadow: "0 0 8px #facc15" }}
        />
        <span className="font-mono text-[10px] tracking-mono-wide text-nau-accent">NAU</span>
      </div>
      <div className="font-mono text-xs leading-7 text-nau-fg">
        <div className="text-nau-fg-dim">// {label}</div>
        <div className="text-nau-accent">[{bar}]</div>
      </div>
    </div>
  );
}
