using System.Text.Json;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Matrix;

public sealed class MatrixCredentials
{
    public string HomeserverUrl { get; init; } = "";
    public string UserId { get; init; } = "";
    public string AccessToken { get; init; } = "";

    public static MatrixCredentials Parse(string credentialsJson)
    {
        var parsed = JsonSerializer.Deserialize<MatrixCredentials>(
            credentialsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.HomeserverUrl)
            || string.IsNullOrWhiteSpace(parsed.AccessToken))
        {
            throw new ArgumentException("Matrix-Credentials unvollständig (homeserverUrl/accessToken erforderlich).");
        }
        return parsed;
    }

    public string NormalizedHomeserver() => HomeserverUrl.TrimEnd('/');
}
