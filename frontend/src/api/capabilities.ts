export interface Capabilities {
  whatsapp: boolean;
}

export async function getCapabilities(): Promise<Capabilities> {
  const res = await fetch("/api/capabilities");
  if (!res.ok) {
    throw new Error(`Capabilities-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as Capabilities;
}
