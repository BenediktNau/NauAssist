namespace NauAssist.Backend.Features.Agent;

/// <summary>
/// Fixe, im Backend verdrahtete Spielregeln für den Agenten — werden bei jedem
/// Lauf vor den User-konfigurierten <c>SystemPrompt</c> gehängt. Hier landet
/// alles, was Tool-Verhalten betrifft (welches Tool wann, Datumsformate,
/// Bestätigungs-Konventionen). Der konfigurierbare SystemPrompt bleibt für die
/// Persona/Stil-Anpassung durch den User reserviert.
/// </summary>
internal static class AgentOperatingRules
{
    public const string Text =
        "[Agent-Spielregeln — verbindlich]\n" +
        "\n" +
        "Tools & Workflows:\n" +
        "- Terminanfrage: rufe lookup_free_slots, wähle 2–3 passende Slots, rufe present_proposals damit, und antworte danach kurz. " +
        "Bestätigt der User einen Slot, rufe create_event.\n" +
        "- Regel-Eingaben (Arbeitszeiten, Pausen, Sperren): rufe add_rule mit strukturierten Args. Zum Entfernen: delete_rule.\n" +
        "- Termin löschen oder verschieben/ändern: erst per get_calendar_range die passende event_id ermitteln, beim User explizit bestätigen lassen, dann delete_event bzw. update_event aufrufen. Niemals ohne Bestätigung löschen oder verschieben.\n" +
        "- update_event nimmt nur die zu ändernden Felder; nicht gesetzte Felder bleiben unverändert. Für ein reines Verschieben reichen start (+ ggf. end).\n" +
        "\n" +
        "Datums-/Zeitformat:\n" +
        "- Aktuelle Zeit, Wochentag und die Daten für 'heute', 'morgen', 'diese/nächste Woche' und 'dieses/nächstes Wochenende' stehen im Zeit-Kontext-Block. " +
        "Verwende immer diese Daten — nie eigene Schätzungen.\n" +
        "- Für ungewöhnliche Bezüge ('in drei Wochen am Donnerstag') rufe get_current_time.\n" +
        "- Ganztägige Einträge (create_event und update_event): is_all_day=true, start/end im Format yyyy-MM-dd, end ist exklusiv (1-Tages-Urlaub am 1.6. → start=2026-06-01, end=2026-06-02).\n" +
        "\n" +
        "Längerfristiger Kontext:\n" +
        "- Wenn ein Block 'Längerfristiger Kontext — All-Day-Termine' erscheint, sind das ganztägige Einträge (Urlaub, Schulung, Reise) im Lookahead. " +
        "Sie blockieren keinen Slot, aber prüfe vor Vorschlägen, ob ein vorgeschlagener Tag mit einem dieser Kontexte kollidiert — frage bei Kollision aktiv nach.";
}
