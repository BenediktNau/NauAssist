using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;

namespace NauAssist.Api.Diagnostics;

/// <summary>
/// Stub für Etappe 0: prüft, ob der konfigurierte Datenpfad beschreibbar ist.
/// Etappe 2 ersetzt das durch eine echte SQLite-Connection-Probe.
/// </summary>
public sealed class MemoryDatabaseHealthCheck : IHealthCheck
{
    private readonly IPathResolver _paths;
    private readonly IOptionsMonitor<MemoryOptions> _options;

    public MemoryDatabaseHealthCheck(IPathResolver paths, IOptionsMonitor<MemoryOptions> options)
    {
        _paths = paths;
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_paths.DataRoot, _options.CurrentValue.DatabaseFile);
        var data = new Dictionary<string, object>
        {
            ["DatabasePath"] = path,
            ["Exists"] = File.Exists(path),
        };

        if (!Directory.Exists(_paths.DataRoot))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"DataRoot '{_paths.DataRoot}' existiert nicht", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            File.Exists(path)
                ? $"Memory-DB vorhanden: {path}"
                : $"Memory-DB noch nicht angelegt — wird in Etappe 2 erzeugt: {path}",
            data));
    }
}
