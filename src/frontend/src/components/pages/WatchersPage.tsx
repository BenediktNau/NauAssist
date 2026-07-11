import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/hooks/queries";
import { PageLoader } from "@/components/nau/PageLoader";
import {
  cancelWatchJob,
  listWatchJobs,
  pauseWatchJob,
  resumeWatchJob,
  type WatchJobDto,
  type WatchJobStatus,
} from "@/api/watch-jobs";

const STATUS_LABEL: Record<WatchJobStatus, string> = {
  active: "AKTIV",
  paused: "PAUSIERT",
  fired: "GEFEUERT",
  completed: "ERLEDIGT",
  failed: "FEHLER",
  expired: "ABGELAUFEN",
};

function statusClasses(status: WatchJobStatus): string {
  switch (status) {
    case "active":
      return "border-nau-accent/40 text-nau-accent";
    case "paused":
      return "border-nau-line text-nau-fg-dim";
    case "fired":
    case "completed":
      return "border-emerald-500/40 text-emerald-400";
    default:
      return "border-red-500/40 text-red-400";
  }
}

function formatTime(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("de-DE", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Watcher-Übersicht: laufende/erledigte Hintergrund-Beobachtungen mit letztem Befund.
 * Angelegt wird per Chat ("sag mir, wenn …"); hier gibt es Pause/Weiter/Stop.
 */
export function WatchersPage() {
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const jobsQuery = useQuery({
    queryKey: queryKeys.watchJobs,
    queryFn: listWatchJobs,
    // Fallback-Polling, falls der /api/events-Stream mal nicht verbunden ist.
    refetchInterval: 60_000,
  });

  const run = async (action: (id: number) => Promise<void>, id: number) => {
    setError(null);
    try {
      await action(id);
      await queryClient.invalidateQueries({ queryKey: queryKeys.watchJobs });
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  if (jobsQuery.isPending) return <PageLoader />;

  const jobs = jobsQuery.data ?? [];

  return (
    <div className="h-full overflow-y-auto px-5 py-6 lg:px-12 lg:py-8">
      <h1 className="m-0 mb-1 font-sans text-3xl font-semibold text-nau-fg">Watcher</h1>
      <p className="mb-6 font-mono text-[11px] tracking-mono text-nau-fg-dim">
        {"// HINTERGRUND-BEOBACHTUNGEN · ANLEGEN PER CHAT („sag mir, wenn …“)"}
      </p>

      {jobsQuery.isError && (
        <div className="mb-4 border border-red-500/40 bg-red-500/10 px-4 py-3 font-mono text-[12px] text-red-400">
          {jobsQuery.error instanceof Error ? jobsQuery.error.message : "Laden fehlgeschlagen"}
        </div>
      )}
      {error && (
        <div className="mb-4 border border-red-500/40 bg-red-500/10 px-4 py-3 font-mono text-[12px] text-red-400">
          {error}
        </div>
      )}

      {jobs.length === 0 ? (
        <div className="border border-nau-line bg-nau-bg-alt p-10 text-center font-mono text-[11px] tracking-mono text-nau-fg-dim">
          {"// KEINE WATCHER — IM CHAT ANLEGEN"}
        </div>
      ) : (
        <ul className="m-0 flex list-none flex-col gap-4 p-0">
          {jobs.map((job: WatchJobDto) => (
            <li key={job.id} className="border border-nau-line bg-nau-bg-alt p-5">
              <div className="mb-2 flex flex-wrap items-center gap-3">
                <span className="font-sans text-base font-semibold text-nau-fg">{job.title}</span>
                <span
                  className={
                    "border px-2 py-0.5 font-mono text-[10px] tracking-mono-wide " +
                    statusClasses(job.status)
                  }
                >
                  {STATUS_LABEL[job.status]}
                </span>
              </div>

              <p className="mb-3 text-sm text-nau-fg-dim">{job.goal}</p>

              {job.lastSummary && (
                <p className="mb-3 border-l-2 border-nau-line pl-3 text-sm text-nau-fg">
                  {job.lastSummary}
                </p>
              )}

              <div className="mb-4 flex flex-wrap gap-x-5 gap-y-1 font-mono text-[11px] tracking-mono text-nau-fg-dim">
                <span>CHECKS {job.checkCount}</span>
                <span>ZULETZT {formatTime(job.lastCheckedAt)}</span>
                {job.status === "active" && <span>NÄCHSTER {formatTime(job.nextDueAt)}</span>}
              </div>

              <div className="flex gap-3">
                {job.status === "active" && (
                  <>
                    <ActionButton label="PAUSE" onClick={() => run(pauseWatchJob, job.id)} />
                    <ActionButton label="STOP" onClick={() => run(cancelWatchJob, job.id)} />
                  </>
                )}
                {job.status === "paused" && (
                  <>
                    <ActionButton label="WEITER" onClick={() => run(resumeWatchJob, job.id)} />
                    <ActionButton label="STOP" onClick={() => run(cancelWatchJob, job.id)} />
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ActionButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="cursor-pointer border border-nau-line bg-transparent px-4 py-1.5 font-mono text-[11px] tracking-mono-wide text-nau-fg-dim transition-colors hover:border-nau-accent hover:text-nau-accent"
    >
      {label}
    </button>
  );
}
