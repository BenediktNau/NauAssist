# WhatsApp-Allowlist: Chats hinzufügen ohne auf eingehende Nachrichten zu warten

> Recherche + Umsetzungsplan, 2026-06-18. Anlass: WhatsApp ist auf NVEICDOC00 aktiviert und
> `connected`, aber es entstehen keine Suggestions. Wahrscheinliche Ursache: die **Allowlist ist
> leer/unvollständig**, weil die Chat-Auswahlliste nur Chats zeigt, die der Sidecar zufällig über
> Live-Events schon „gesehen" hat. Heute gibt es **keinen aktiven Weg**, einen Chat in die Liste zu
> holen — man muss warten, bis jemand schreibt. Dieser Plan schließt diese Bootstrap-Lücke.

## Ausgangslage (Ist-Zustand)

- Sidecar (`src/sidecar/src/baileys-manager.ts`) sammelt Chat-Namen **nur passiv** aus Live-Events:
  `chats.upsert`, `contacts.upsert`, `messaging-history.set` und `pushName` eingehender Nachrichten.
- Diese Namen liegen in einer **In-Memory-Map** (`session.chats`) — nicht persistiert. Nach jedem
  Sidecar-Neustart ist sie leer (`restoreAll()` reconnectet, aber die Map startet bei null).
- `makeWASocket({ syncFullHistory: false })` → WhatsApp pusht beim Connect **keine** volle Chatliste.
- Folge: Direkt nach dem Pairing (oder nach Neustart) ist die Auswahlliste in `WhatsAppSection.tsx`
  oft leer. UI sagt selbst: *„Schreib der Nummer einmal, dann neu laden."*
- **Es gibt kein manuelles JID-Eingabefeld.** Taucht ein Chat nicht von selbst auf → nicht hinzufügbar.
- Harte Konsequenz: leere Allowlist ⇒ `WhatsAppObserver.cs:50` überspringt den **ganzen Account**.

## Recherche: was Baileys 6.7.23 tatsächlich kann (geprüft in node_modules)

| Methode / Option | Liefert | Eignung für Bootstrap |
| --- | --- | --- |
| **`sock.groupFetchAllParticipating()`** | `{ [jid]: GroupMetadata }` — **alle** Gruppen inkl. `subject` (Name), on demand | ★ Beste sofortige Lösung für **Gruppen** — keine Wartezeit, ein Aufruf nach Connect liefert die komplette Gruppenliste. |
| **`sock.onWhatsApp(...nummern)`** | `[{ jid, exists, lid }]` — validiert Nummern, gibt kanonische JID **und** LID zurück | ★ Ermöglicht **manuelle Eingabe** per Telefonnummer für 1:1-Chats; nebenbei Fix fürs `@lid`↔`@s.whatsapp.net`-Matching (beide Formen bekannt). |
| **`syncFullHistory: true`** (+ `shouldSyncHistoryMessage`) | Voller `messaging-history.set` beim Connect: `chats[]` + `contacts[]` | Füllt die **komplette** 1:1-/Kontaktliste automatisch. Trade-off: schwererer Initial-Sync, mehr Datenverkehr, langsamerer Connect. History-Messages filtert der Manager ohnehin (`type==="notify"`). |
| **Chat-Map in `buffer.db` persistieren** | Entdeckte `{chatId,name}` überleben Neustart | Komplementär: verhindert, dass die Liste nach jedem Sidecar-Restart wieder leer ist. |
| `sock.fetchStatus` / `contacts.upsert` | Einzel-Status / Kontakte | Geringer Mehrwert für die Auswahl, nicht nötig. |

Wichtig: `onWhatsApp` gibt **`lid`** zurück — das bestätigt die Matching-Hypothese aus dem
Diagnose-Plan: moderne WhatsApp-JIDs existieren in zwei Formen (`…@s.whatsapp.net` und `…@lid`).
Wenn die Allowlist die eine Form speichert, eingehende Nachrichten aber die andere tragen, matcht
`WhatsAppObserver` (Ordinal, exakt) nie.

## Empfohlener Ansatz (gestaffelt, kleinste Wins zuerst)

### A. Gruppen aktiv laden — `groupFetchAllParticipating()`  *(kleinster, größter Win)*
In `BaileysManager` eine Methode ergänzen, die bei `listChats(id)` (oder einmal nach `connection==="open"`)
`sock.groupFetchAllParticipating()` aufruft und die Ergebnisse in `session.chats` mergt
(`jid → metadata.subject`). Damit erscheinen **alle Gruppen sofort**, ohne dass jemand schreiben muss.
- Touch-Point: `src/sidecar/src/baileys-manager.ts` (neue `refreshGroups()`, Aufruf in `listChats`/`open`).
- Kein API-Vertragsbruch — `GET /sessions/:id/chats` liefert einfach mehr Einträge.

### B. Manuelle Chat-Eingabe per Nummer — `onWhatsApp()`  *(schließt die Lücke dauerhaft)*
1. Sidecar: neuer Endpoint `POST /sessions/:id/resolve { phone }` → ruft `sock.onWhatsApp(phone)` →
   gibt `{ chatId: jid, lid, exists }` zurück. (`src/sidecar/src/index.ts` + Manager-Methode.)
2. Backend: Helfer-Endpoint `POST /api/source-accounts/whatsapp/session/{id}/resolve` durchreichen
   (`SourceAccountsEndpoints.cs`, im `Enabled`-Block) + Client-Methode in `WhatsAppSidecarClient`.
3. Frontend: in `WhatsAppSection.tsx` (beide Stellen — AddForm **und** „CHATS VERWALTEN") ein
   Eingabefeld „Chat per Nummer hinzufügen" → resolve → Treffer als wählbaren Chat in die Liste +
   direkt in die `draftAllowlist`. (Validierung: `exists` muss truthy sein.)

### C. Allowlist-Matching robust machen — JID-Normalisierung  *(behebt „connected, aber nichts")*
In `WhatsAppObserver` den Allowlist-Vergleich (`:78`, `:82`) um eine Normalisierung erweitern:
User-Teil vor `@` vergleichen und `@lid`/`@s.whatsapp.net` als gleichwertig behandeln. Beim Speichern
der Allowlist konsistent dieselbe Normalform ablegen (idealerweise beide Formen aus `onWhatsApp` kennen).
- Touch-Point: `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/WhatsAppObserver.cs`.
- **TDD zuerst:** `WhatsAppObserverTests` — eingehende `…@lid`-Nachricht gegen `…@s.whatsapp.net`-Allowlist
  muss matchen.

### D. Chat-Map persistieren  *(optional, gegen „nach Neustart wieder leer")*
Entdeckte `{sessionId, chatId, name}` in einer kleinen Tabelle in `buffer.db` ablegen und beim Start
in `session.chats` zurückladen. (`src/sidecar/src/buffer.ts` + `baileys-manager.ts`.)

### E. `syncFullHistory: true`  *(optional, nur wenn A+B nicht reichen)*
Schalter erwägen, wenn die vollständige 1:1-Liste automatisch gewünscht ist. Bewusst gegen die
Trade-offs (Connect-Dauer, Datenvolumen) abwägen. A + B decken den Bedarf vermutlich schon ab.

## Reihenfolge & Aufwand

1. **C** (Matching-Fix) — höchste Priorität, denn selbst eine korrekt befüllte Allowlist nützt nichts,
   wenn `@lid` nicht matcht. Klein, testbar, behebt vermutlich das akute „keine Nachrichten".
2. **A** (Gruppen laden) — sehr klein, sofortiger UX-Gewinn.
3. **B** (manuelle Eingabe) — mittel (Sidecar + Backend + Frontend), schließt die Lücke endgültig.
4. **D** / **E** — optional, je nach Bedarf.

Jeder Schritt ist für sich baubar/testbar und ändert keine bestehenden Verträge destruktiv.

## Verifikation (End-to-End)

1. **C:** Unit-Test `@lid`↔`@s.whatsapp.net` grün; danach reale Termin-Nachricht aus dem freigegebenen
   Chat → `POST /api/suggestions/poll-now` → Suggestion erscheint.
2. **A:** „CHATS VERWALTEN → AKTUALISIEREN" zeigt **direkt nach Pairing** alle Gruppen, ohne dass jemand
   geschrieben hat.
3. **B:** Nummer ins neue Feld eintragen → Chat erscheint wählbar → anhaken → speichern → Card zeigt
   „// n Chat(s) freigegeben" > 0.
4. Regression: `dotnet test src/Backend.Tests` + Sidecar-Build/Smoke grün.
5. Doku in BenediktsMind (Doings-Board + Notiz) gemäß Projekt-Konvention nachziehen.

## Verweise

- Diagnose-Runbook (10 Drop-Punkte der Pipeline): `~/.claude/plans/happy-forging-hippo.md`.
- Ursprüngliches Design: `docs/superpowers/specs/2026-05-30-whatsapp-baileys-sidecar-design.md`.
- Code: `src/sidecar/src/{baileys-manager,index,buffer}.ts`,
  `src/Backend/Features/AutonomousAgent/Sources/WhatsApp/*`, `src/frontend/src/components/settings/WhatsAppSection.tsx`.
