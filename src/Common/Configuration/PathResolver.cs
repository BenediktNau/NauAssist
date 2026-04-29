using System;
using System.IO;
using Microsoft.Extensions.Options;

namespace NauAssist.Common.Configuration;

/// <summary>
/// Standard-Implementierung von <see cref="IPathResolver"/>. Kanonisiert
/// alle Pfade über <see cref="Path.GetFullPath(string)"/> und legt
/// <c>DataRoot</c>, <c>LogsRoot</c>, <c>ModelsRoot</c> beim ersten Zugriff
/// automatisch an. <c>CoreRoot</c> und <c>ExtensionsRoot</c> müssen
/// existieren — fehlt eines, schlägt der Konstruktor hart fehl.
/// </summary>
public sealed class PathResolver : IPathResolver
{
    private readonly Lazy<string> _dataRoot;
    private readonly Lazy<string> _logsRoot;
    private readonly Lazy<string> _modelsRoot;

    public PathResolver(IOptions<PathOptions> options)
    {
        var opts = options.Value;
        var baseDir = ResolveBaseDirectory(opts.BaseDirectory);

        CoreRoot = ResolveExisting(baseDir, opts.CoreRoot, nameof(opts.CoreRoot));
        ExtensionsRoot = ResolveExisting(baseDir, opts.ExtensionsRoot, nameof(opts.ExtensionsRoot));

        _dataRoot = new Lazy<string>(() => EnsureDirectory(baseDir, opts.DataRoot));
        _logsRoot = new Lazy<string>(() => EnsureDirectory(baseDir, opts.LogsRoot));
        _modelsRoot = new Lazy<string>(() => EnsureDirectory(baseDir, opts.ModelsRoot));
    }

    /// <summary>
    /// Auflösung in dieser Reihenfolge: explizite Konfiguration → aufwärts­
    /// suche nach <c>NauAssist.slnx</c> ab <see cref="AppContext.BaseDirectory"/>
    /// → aktuelles Arbeitsverzeichnis. Damit funktioniert der Resolver in
    /// Dev (CWD beliebig, slnx wird gefunden) und im Container (BaseDirectory
    /// per ENV gesetzt) ohne Sonderfälle.
    /// </summary>
    private static string ResolveBaseDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (current.GetFiles("NauAssist.slnx").Length > 0)
            {
                return current.Parent?.FullName ?? current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    public string CoreRoot { get; }

    public string ExtensionsRoot { get; }

    public string DataRoot => _dataRoot.Value;

    public string LogsRoot => _logsRoot.Value;

    public string ModelsRoot => _modelsRoot.Value;

    private static string Canonicalize(string baseDir, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, baseDir);
    }

    private static string ResolveExisting(string baseDir, string path, string optionName)
    {
        var canonical = Canonicalize(baseDir, path);
        if (!Directory.Exists(canonical))
        {
            throw new DirectoryNotFoundException(
                $"Pfad-Option '{optionName}' verweist auf nicht existierendes Verzeichnis: {canonical}");
        }

        return canonical;
    }

    private static string EnsureDirectory(string baseDir, string path)
    {
        var canonical = Canonicalize(baseDir, path);
        Directory.CreateDirectory(canonical);
        return canonical;
    }
}
