export type WatchJobStatus =
  | "active"
  | "paused"
  | "fired"
  | "completed"
  | "failed"
  | "expired";

export interface WatchJobDto {
  id: number;
  title: string;
  goal: string;
  kind: string;
  status: WatchJobStatus;
  checkCount: number;
  lastCheckedAt: string | null;
  nextDueAt: string;
  lastSummary: string | null;
  createdAt: string;
}

export async function listWatchJobs(): Promise<WatchJobDto[]> {
  const res = await fetch("/api/watch-jobs");
  if (!res.ok) throw new Error(`Watch-Jobs-Load fehlgeschlagen: HTTP ${res.status}`);
  return (await res.json()) as WatchJobDto[];
}

async function postAction(id: number, action: "pause" | "resume" | "cancel"): Promise<void> {
  const res = await fetch(`/api/watch-jobs/${id}/${action}`, { method: "POST" });
  if (!res.ok) throw new Error(`${action} fehlgeschlagen: HTTP ${res.status}`);
}

export const pauseWatchJob = (id: number) => postAction(id, "pause");
export const resumeWatchJob = (id: number) => postAction(id, "resume");
export const cancelWatchJob = (id: number) => postAction(id, "cancel");
