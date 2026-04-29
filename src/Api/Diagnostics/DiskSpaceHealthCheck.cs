using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NauAssist.Common.Configuration;

namespace NauAssist.Api.Diagnostics;

public sealed class DiskSpaceHealthCheck : IHealthCheck
{
    private const long DegradedThresholdBytes = 1L * 1024 * 1024 * 1024;
    private const long UnhealthyThresholdBytes = 100L * 1024 * 1024;

    private readonly IPathResolver _paths;

    public DiskSpaceHealthCheck(IPathResolver paths)
    {
        _paths = paths;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dataRoot = _paths.DataRoot;
        var driveRoot = Path.GetPathRoot(dataRoot);
        if (string.IsNullOrEmpty(driveRoot))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Kein Drive-Root für DataRoot '{dataRoot}' bestimmbar"));
        }

        var drive = new DriveInfo(driveRoot);
        var free = drive.AvailableFreeSpace;
        var data = new Dictionary<string, object>
        {
            ["DataRoot"] = dataRoot,
            ["FreeBytes"] = free,
            ["FreeMegabytes"] = free / (1024 * 1024),
        };

        if (free < UnhealthyThresholdBytes)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Nur noch {free / (1024 * 1024)} MB frei auf {driveRoot}", data: data));
        }

        if (free < DegradedThresholdBytes)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Wenig Speicher: {free / (1024 * 1024)} MB frei", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"{free / (1024 * 1024)} MB frei auf {driveRoot}", data));
    }
}
