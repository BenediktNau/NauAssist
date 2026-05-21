namespace NauAssist.Backend.Features.Settings;

public interface IAppSettingsRepository
{
    Task<LlmSettings> GetLlmAsync(CancellationToken ct);
    Task SetLlmAsync(LlmSettings settings, CancellationToken ct);
}
