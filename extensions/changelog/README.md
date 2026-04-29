# changelog/

Audit-Log aller Schreibvorgänge in die Erweiterungs-Welt.
`IExtensionAuditLog` schreibt eine JSON-Zeile pro Operation in
`{yyyy-MM-dd}.jsonl`. Felder: `actor`, `operation`, `path`, `time`,
`metadata`.
