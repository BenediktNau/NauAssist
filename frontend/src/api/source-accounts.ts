export interface MatrixCredentialsInput {
  homeserverUrl: string;
  userId: string;
  accessToken: string;
}

export interface SourceAccountDto {
  id: number;
  kind: string;
  displayName: string;
  credentials: Record<string, string | null>;
  allowlist: string[];
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface MatrixRoomDto {
  roomId: string;
  displayName: string | null;
}

export async function listSourceAccounts(kind?: string): Promise<SourceAccountDto[]> {
  const url = kind ? `/api/source-accounts/?kind=${kind}` : "/api/source-accounts/";
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`Accounts-Load fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SourceAccountDto[];
}

export async function createMatrixAccount(
  displayName: string,
  credentials: MatrixCredentialsInput,
  allowlist: string[],
): Promise<SourceAccountDto> {
  const res = await fetch("/api/source-accounts/", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      kind: "matrix",
      displayName,
      credentials,
      allowlist,
    }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Account-Anlage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SourceAccountDto;
}

export async function updateSourceAccount(
  id: number,
  patch: {
    displayName?: string;
    credentials?: MatrixCredentialsInput;
    allowlist?: string[];
    enabled?: boolean;
  },
): Promise<SourceAccountDto> {
  const res = await fetch(`/api/source-accounts/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(patch),
  });
  if (!res.ok) {
    throw new Error(`Account-Update fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SourceAccountDto;
}

export async function deleteSourceAccount(id: number): Promise<void> {
  const res = await fetch(`/api/source-accounts/${id}`, { method: "DELETE" });
  if (!res.ok && res.status !== 404) {
    throw new Error(`Account-Delete fehlgeschlagen: HTTP ${res.status}`);
  }
}

export async function listMatrixRooms(
  credentials: MatrixCredentialsInput,
): Promise<MatrixRoomDto[]> {
  const res = await fetch("/api/source-accounts/matrix/list-rooms", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ credentials }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `Matrix-Anfrage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as MatrixRoomDto[];
}

export async function listMatrixRoomsForAccount(id: number): Promise<MatrixRoomDto[]> {
  const res = await fetch(`/api/source-accounts/${id}/matrix/rooms`);
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `Matrix-Anfrage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as MatrixRoomDto[];
}

// --- IMAP ---

export interface ImapCredentialsInput {
  imapHost: string;
  imapPort: number;
  imapSsl: boolean;
  smtpHost: string;
  smtpPort: number;
  smtpStartTls: boolean;
  username: string;
  password: string;
  fromAddress?: string;
  fromName?: string;
}

export async function listImapFolders(
  credentials: ImapCredentialsInput,
): Promise<string[]> {
  const res = await fetch("/api/source-accounts/imap/list-folders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ credentials }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `IMAP-Anfrage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as string[];
}

export async function listImapFoldersForAccount(id: number): Promise<string[]> {
  const res = await fetch(`/api/source-accounts/${id}/imap/folders`);
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `IMAP-Anfrage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as string[];
}

export async function createImapAccount(
  displayName: string,
  credentials: ImapCredentialsInput,
  allowlist: string[],
): Promise<SourceAccountDto> {
  const res = await fetch("/api/source-accounts/", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ kind: "imap", displayName, credentials, allowlist }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Account-Anlage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SourceAccountDto;
}
