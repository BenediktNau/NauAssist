using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateOllamaSettings;

public sealed class UpdateOllamaSettingsHandler
    : IRequestHandler<UpdateOllamaSettingsRequest, UpdateOllamaSettingsResult>
{
    private readonly IAppSettingsRepository _settings;

    public UpdateOllamaSettingsHandler(IAppSettingsRepository settings)
    {
        _settings = settings;
    }

    public async ValueTask<UpdateOllamaSettingsResult> Handle(
        UpdateOllamaSettingsRequest request,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Host, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return new UpdateOllamaSettingsResult(false,
                "Host muss eine absolute http(s)-URL sein.");
        }

        if (request.NumCtx <= 0 || request.NumCtx > 1_000_000)
        {
            return new UpdateOllamaSettingsResult(false,
                "NumCtx muss zwischen 1 und 1.000.000 liegen.");
        }

        if (request.Temperature < 0.0 || request.Temperature > 2.0)
        {
            return new UpdateOllamaSettingsResult(false,
                "Temperature muss zwischen 0.0 und 2.0 liegen.");
        }

        var existing = await _settings.GetOllamaAsync(ct);

        string? newKey = request.ApiKey switch
        {
            null => existing.ApiKey,
            ""   => null,
            _    => request.ApiKey,
        };

        await _settings.SetOllamaAsync(
            new OllamaUserSettings(request.Host, newKey, request.NumCtx, request.Temperature),
            ct);

        return new UpdateOllamaSettingsResult(true, null);
    }
}
