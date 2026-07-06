namespace NauAssist.Backend.Features.Agent;

/// <summary>
/// Fixe, im Backend verdrahtete Spielregeln für den Agenten — werden bei jedem
/// Lauf vor den User-konfigurierten <c>SystemPrompt</c> gehängt. Hier landet
/// alles, was Tool-Verhalten betrifft (welches Tool wann, Datumsformate,
/// Bestätigungs-Konventionen). Der konfigurierbare SystemPrompt bleibt für die
/// Persona/Stil-Anpassung durch den User reserviert. Absätze zu optionalen Tools (Watch-Jobs, Web)
/// erscheinen nur, wenn die Tools tatsächlich registriert sind — sonst
/// halluziniert das LLM Calls auf nicht existente Tools.
/// </summary>
internal static class AgentOperatingRules
{
    public static string Compose(IEnumerable<string> toolNames)
    {
        var tools = toolNames as ISet<string> ?? new HashSet<string>(toolNames, StringComparer.Ordinal);

        var text = Header + BaseToolRules;
        if (tools.Contains("create_watch_job")) text += WatchJobRules;
        if (tools.Contains("web_search")) text += WebRules;
        text += DateTimeRules;
        return text;
    }

    private const string Header =
        "[Agent-Spielregeln — verbindlich]\n" +
        "\n" +
        "Tools & Workflows:\n";

    private const string BaseToolRules =
        "- Terminanfrage: rufe lookup_free_slots, wähle 2–3 passende Slots, rufe present_proposals damit, und antworte danach kurz. " +
        "Bestätigt der User einen Slot, rufe create_event.\n" +
        "- Regel-Eingaben (Arbeitszeiten, Pausen, Sperren): rufe add_rule mit strukturierten Args. Zum Entfernen: delete_rule.\n" +
        "- Termin löschen oder verschieben/ändern: erst per get_calendar_range die passende event_id ermitteln, beim User explizit bestätigen lassen, dann delete_event bzw. update_event aufrufen. Niemals ohne Bestätigung löschen oder verschieben.\n" +
        "- update_event nimmt nur die zu ändernden Felder; nicht gesetzte Felder bleiben unverändert. Für ein reines Verschieben reichen start (+ ggf. end).\n" +
        "- Serien-Instanzen: get_calendar_range liefert pro Termin is_series_instance und series_id. Ist is_series_instance=true, frage den User vor delete_event/update_event explizit, ob nur diese Instanz (scope='instance') oder die gesamte Serie (scope='series') gemeint ist, und gib den scope im Tool-Call mit. Default (scope weglassen) wirkt nur auf die einzelne Instanz. 'scope=series' ändert/löscht den Master und damit alle Instanzen — auch vergangene; weise darauf hin, wenn der User das möglicherweise nicht will. 'Ab jetzt für die Zukunft verschieben/abschaffen' wird nicht direkt unterstützt; in dem Fall beim User rückfragen und ggf. die Serie löschen und eine neue ab Datum X anlegen.\n";

    private const string WatchJobRules =
        "- Beobachtungs-/Watch-Aufträge ('sag mir, wenn …', 'überwache …', 'benachrichtige mich, sobald … wieder verfügbar/im Angebot ist'): formuliere ein präzises goal + successCriteria + judgeQuestion, wähle sinnvolle searchQueries (bei Bedarf nach bevorzugten Shops/URLs fragen) und rufe create_watch_job. Bestätige danach kurz, dass im Hintergrund regelmäßig geprüft wird und der User normal weiterreden kann — der Watcher meldet sich von selbst per Push. 'Was überwachst du gerade?' → list_watch_jobs. 'Stopp/Pausiere die Beobachtung …' → cancel_watch_job (mode='cancel' bzw. 'pause').\n";

    private const string WebRules =
        "- Fragen nach aktuellen Informationen (News, Preise, Öffnungszeiten, Verfügbarkeiten, Fakten außerhalb deines Wissens): " +
        "rufe web_search mit einer präzisen Suchanfrage. Reichen die Snippets nicht, lies die vielversprechendste URL per fetch_webpage. " +
        "Nenne in der Antwort die Quelle (URL). Keine Web-Suche für Kalender-/Regel-Aktionen oder Smalltalk.\n";

    private const string DateTimeRules =
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
