export interface AuthCapabilities {
  enabled: boolean;
  loginUrl: string;
}

export interface Capabilities {
  whatsApp: boolean;
  auth: AuthCapabilities;
  watchJobs: boolean;
}

export async function getCapabilities(): Promise<Capabilities> {
  const res = await fetch("/api/capabilities");
  if (!res.ok) {
    throw new Error(`Capabilities-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as Capabilities;
}
