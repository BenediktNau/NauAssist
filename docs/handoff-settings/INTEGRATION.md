# SettingsPage → Master-Detail · Integration

Baut die eine lange Settings-Seite zu einem **Master-Detail**-Layout um:
- **Desktop (lg+):** gruppierte Sidebar (`AGENT` / `KANÄLE` / `KONTO`) + rechts nur die aktive Sektion.
- **Mobile (< lg):** gruppierte Kategorie-Liste → Tap öffnet die Sektion mit „‹ Einstellungen“-Zurück.

**Wichtig:** Es ändert sich praktisch nur die äußere `SettingsPage`-Funktion. Alle
Sektions- und Helfer-Komponenten bleiben unverändert und werden lediglich **einzeln**
statt gesammelt gerendert.

---

## Schritte (alles in `src/components/pages/SettingsPage.tsx`)

### 1. Imports erweitern
```diff
- import { useEffect, useRef, useState } from "react";
- import type { ReactNode } from "react";
- import { ArrowLeft } from "lucide-react";
+ import { useEffect, useMemo, useRef, useState } from "react";
+ import type { ComponentType, ReactNode } from "react";
+ import { ArrowLeft, ChevronRight, Cpu, CalendarDays, User, Bell, Mail, MessageCircle, LogOut } from "lucide-react";
```
> `useAuth`, alle `@/api/...`-Imports und die `*Section`-Imports bleiben wie sie sind.

### 2. Navigations-Modell einfügen
Aus `SettingsPage.partial.tsx` die Blöcke **`type SectionKey`**, **`NAV_META`**,
**`NavGroup`**, **`buildGroups()`** und **`SettingsLoading()`** übernehmen
(z.B. direkt oberhalb von `export function SettingsPage`).

### 3. `SettingsPage`-Funktion komplett ersetzen
Die bisherige `export function SettingsPage(...) { ... }` (inkl. des alten
`navItems`-Arrays und des kompletten `return (...)`) durch die Version aus
`SettingsPage.partial.tsx` ersetzen.

### 4. Unverändert lassen
`Row`, `SectionHead`, `TextInput`, `PrimaryButton`, `SecondaryButton`,
`ModelCombobox`, `LlmSection`, `CalendarSection`, `AccountFooter` — sowie die
ausgelagerten `PersonaSection` / `PushSection` / `ImapSection` / `WhatsAppSection`.

---

## Wie es funktioniert
- Ein einziger State `active: SectionKey | null` steuert beides:
  - **Mobile:** `null` → Liste, gesetzt → Detail (`setActive(null)` = zurück).
  - **Desktop:** zeigt immer ein Detail; `current = active ?? "llm"` als Default,
    Sidebar-Klick setzt `active`.
- Das Detail (`<main>`) wird **einmal** gerendert:
  `active === null ? "hidden lg:block" : "block"` → auf Mobile nur sichtbar, wenn
  eine Sektion gewählt ist; auf Desktop immer.
- `buildGroups()` blendet **WhatsApp** nur bei `caps.whatsApp` und **Konto** nur bei
  aktivierter Auth ein — identisch zur bisherigen Logik.

---

## Kleine Aufräum-Empfehlungen (optional, kosmetisch)
Diese Sektionen hatten Abstände, die nur im alten „alles-untereinander“-Layout Sinn
ergaben. Standalone wirken sie sauberer ohne:

1. **`CalendarSection`** (in dieser Datei) — Wrapper-`mt-14` entfernen:
   ```diff
   - <div id="section-calendar" className="mt-14">
   + <div id="section-calendar">
   ```
2. **`AccountFooter`** (in dieser Datei) — als eigene „Konto“-Sektion braucht es die
   obere Trennlinie + großen Top-Abstand nicht mehr:
   ```diff
   - <div className="mt-14 flex items-center justify-between border-t border-nau-line pt-6">
   + <div className="flex flex-col items-start gap-4">
   ```
   (Inhalt – „ANGEMELDET ALS …“ + Abmelden-Button – bleibt.)

> Die `anchor`-Props (`section-persona` etc.) werden weiter durchgereicht und
> schaden nicht; die Anker-Sprung-Navigation entfällt, weil immer nur eine Sektion
> sichtbar ist.

## Nicht enthalten (bewusst)
Keine sticky „Speichern“-Leiste — die Speichern-Buttons bleiben im Fluss der Sektion,
wie im abgestimmten Prototyp.
```
