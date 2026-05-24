using Mediator;

namespace NauAssist.Backend.Features.Settings.UpdateLlmSettings;

public sealed record UpdateLlmSettingsRequest(string OllamaModel) : IRequest<UpdateLlmSettingsResult>;

public sealed record UpdateLlmSettingsResult(bool Ok, string? Error);
