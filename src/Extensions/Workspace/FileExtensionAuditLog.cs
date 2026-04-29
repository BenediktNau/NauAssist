using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NauAssist.Common.Configuration;

namespace NauAssist.Extensions.Workspace;

public sealed class FileExtensionAuditLog : IExtensionAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IPathResolver _paths;
    private readonly TimeProvider _clock;
    private readonly object _writeLock = new();

    public FileExtensionAuditLog(IPathResolver paths, TimeProvider? clock = null)
    {
        _paths = paths;
        _clock = clock ?? TimeProvider.System;
    }

    public void Append(string actor, string operation, string canonicalPath, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var changelogDir = Path.Combine(_paths.ExtensionsRoot, "changelog");
        Directory.CreateDirectory(changelogDir);

        var now = _clock.GetUtcNow();
        var file = Path.Combine(changelogDir, $"{now:yyyy-MM-dd}.jsonl");

        var entry = new AuditEntry(
            Time: now,
            Actor: actor,
            Operation: operation,
            Path: canonicalPath,
            Metadata: metadata);

        var line = JsonSerializer.Serialize(entry, JsonOptions);

        lock (_writeLock)
        {
            File.AppendAllText(file, line + Environment.NewLine);
        }
    }

    private sealed record AuditEntry(
        DateTimeOffset Time,
        string Actor,
        string Operation,
        string Path,
        IReadOnlyDictionary<string, string>? Metadata);
}
