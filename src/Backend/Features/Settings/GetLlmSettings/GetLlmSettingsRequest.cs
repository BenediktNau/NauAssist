using Mediator;

namespace NauAssist.Backend.Features.Settings.GetLlmSettings;

public sealed record GetLlmSettingsRequest : IRequest<GetLlmSettingsResponse>;

public sealed record GetLlmSettingsResponse(string OllamaModel);
