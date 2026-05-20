using Mediator;

namespace NauAssist.Backend.Features.Chat.SendMessage;

public sealed record SendMessageRequest(string SessionId, string UserText) : IStreamCommand<SseEvent>;
