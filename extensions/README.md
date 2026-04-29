# Erweiterungs-Welt

Territorium des Agenten. Hier — und **nur** hier — darf der Agent zur
Laufzeit schreiben. Schreibzugriffe laufen ausschließlich über
`IExtensionWorkspace` (siehe `src/Extensions/`); jede Operation wird in
`changelog/` als JSONL-Zeile protokolliert.

Das Layout ist Spiegel des Konzepts (§6 im Handout): Tools, ihre
Versionen und Specs, Schwächen-Liste, Nutzungs-Logs und auftrags­
bezogene Phase-2-Komponenten. Konzeptuell bildet dieser Ordner die
**fünfte Schicht des Gedächtnisses** — eine Schicht aus Code statt
Daten. Beim Backup der „Persönlichkeit" reist `extensions/` mit den
SQLite-Dateien.

## Unterordner

| Ordner            | Zweck                                                  |
|-------------------|--------------------------------------------------------|
| `tools/`          | Vom Agenten erzeugte Werkzeuge, versioniert pro Tool   |
| `specifications/` | Lesbare Tool-Specs, eine pro Tool                      |
| `changelog/`      | Audit-Log aller Schreibvorgänge (JSONL pro Tag)        |
| `weakness_log/`   | Schwächen-Liste für den Vorschlags-Modus               |
| `usage_logs/`     | Tool-Aufrufe, Häufigkeit, Fehler                       |
| `phase2_jobs/`    | Auftragsbezogene Komponenten (siehe Konzept §5)        |
