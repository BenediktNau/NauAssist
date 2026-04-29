using System.Collections.Generic;

namespace NauAssist.Extensions.Workspace;

/// <summary>
/// Append-only Tagebuch über jede Schreiboperation in der Erweiterungs-Welt.
/// Eine JSONL-Datei pro Tag in <c>extensions/changelog/</c>. Konzept §6:
/// die Erweiterungs-Welt ist „eine fünfte Schicht des Gedächtnisses" —
/// ohne Audit kein Gedächtnis.
/// </summary>
public interface IExtensionAuditLog
{
    void Append(string actor, string operation, string canonicalPath, IReadOnlyDictionary<string, string>? metadata = null);
}
