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

// --- WhatsApp ---

export interface WhatsAppCredentialsInput {
  sessionId: string;
  phoneLabel: string;
}

export interface WhatsAppSessionDto {
  sessionId: string;
  state: string;
}

export interface WhatsAppSessionStatusDto {
  state: string;
  qr: string | null;
  phone: string | null;
}

export interface WhatsAppChatDto {
  chatId: string;
  name: string;
}

export async function startWhatsAppSession(sessionId?: string): Promise<WhatsAppSessionDto> {
  const res = await fetch("/api/source-accounts/whatsapp/start", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ sessionId }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `WhatsApp-Start fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as WhatsAppSessionDto;
}

export async function getWhatsAppSession(sessionId: string): Promise<WhatsAppSessionStatusDto> {
  const res = await fetch(`/api/source-accounts/whatsapp/session/${encodeURIComponent(sessionId)}`);
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `WhatsApp-Status fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as WhatsAppSessionStatusDto;
}

export async function listWhatsAppChats(sessionId: string): Promise<WhatsAppChatDto[]> {
  const res = await fetch(`/api/source-accounts/whatsapp/session/${encodeURIComponent(sessionId)}/chats`);
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `WhatsApp-Chats fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as WhatsAppChatDto[];
}

export async function listWhatsAppChatsForAccount(id: number): Promise<WhatsAppChatDto[]> {
  const res = await fetch(`/api/source-accounts/${id}/whatsapp/chats`);
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string; detail?: string };
    throw new Error(body.detail ?? body.error ?? `WhatsApp-Chats fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as WhatsAppChatDto[];
}

export async function deleteWhatsAppSession(sessionId: string): Promise<void> {
  const res = await fetch(`/api/source-accounts/whatsapp/session/${encodeURIComponent(sessionId)}`, {
    method: "DELETE",
  });
  if (!res.ok && res.status !== 404) {
    throw new Error(`WhatsApp-Session-Delete fehlgeschlagen: HTTP ${res.status}`);
  }
}

export async function createWhatsAppAccount(
  displayName: string,
  credentials: WhatsAppCredentialsInput,
  allowlist: string[],
): Promise<SourceAccountDto> {
  const res = await fetch("/api/source-accounts/", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ kind: "whatsapp", displayName, credentials, allowlist }),
  });
  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Account-Anlage fehlgeschlagen: HTTP ${res.status}`);
  }
  return (await res.json()) as SourceAccountDto;
}
