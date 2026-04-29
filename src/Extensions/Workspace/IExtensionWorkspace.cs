using System;
using System.Threading;
using System.Threading.Tasks;

namespace NauAssist.Extensions.Workspace;

/// <summary>
/// Einziger Schreibpfad in die Erweiterungs-Welt. Lesen ist überall in
/// <c>extensions/</c> erlaubt; Schreiben durchläuft Kanonisierung,
/// Boundary-Check und Audit-Log.
/// </summary>
public interface IExtensionWorkspace
{
    /// <summary>Wurzel der Erweiterungs-Welt (absolut, kanonisiert).</summary>
    string Root { get; }

    Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, string actor, CancellationToken cancellationToken = default);

    Task WriteTextAsync(string relativePath, string content, string actor, CancellationToken cancellationToken = default);

    void CreateDirectory(string relativePath, string actor);

    void DeleteFile(string relativePath, string actor);

    bool Exists(string relativePath);

    string ReadAllText(string relativePath);
}
