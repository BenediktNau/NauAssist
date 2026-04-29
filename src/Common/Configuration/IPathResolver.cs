namespace NauAssist.Common.Configuration;

/// <summary>
/// Liefert kanonische, absolute Pfade für die fünf logischen Wurzeln des
/// Systems. Alle Pfade fließen durch diesen Service — siehe Konzept §7
/// (Portabilität als Architekturprinzip).
/// </summary>
public interface IPathResolver
{
    /// <summary>Wurzel der Kernwelt (typischerweise <c>src/</c>). Read-only für den Agenten.</summary>
    string CoreRoot { get; }

    /// <summary>Wurzel der Erweiterungs-Welt (<c>extensions/</c>). Einziger Schreibbereich des Agenten.</summary>
    string ExtensionsRoot { get; }

    /// <summary>Persistente Daten (SQLite-Dateien usw.). Wird beim ersten Zugriff angelegt, falls nicht vorhanden.</summary>
    string DataRoot { get; }

    /// <summary>Log-Ablage. Wird beim ersten Zugriff angelegt, falls nicht vorhanden.</summary>
    string LogsRoot { get; }

    /// <summary>ML-Modelle (Whisper, Piper, Embedding-Modelle …). Wird beim ersten Zugriff angelegt.</summary>
    string ModelsRoot { get; }
}
