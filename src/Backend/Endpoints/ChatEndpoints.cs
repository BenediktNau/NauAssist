using Mediator;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Chat.ChatHistory;
using NauAssist.Backend.Features.Chat.SendMessage;

namespace NauAssist.Backend.Endpoints;

public static class ChatEndpoints
{
    private const string DefaultSessionId = "default";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
            SendMessagePayload payload,
            IMediator mediator,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var writer = new SseWriter(ctx.Response.Body);
            var request = new SendMessageRequest(DefaultSessionId, payload.Message);

            await foreach (var ev in mediator.CreateStream(request, ct).WithCancellation(ct))
            {
                await writer.WriteAsync(ev, ct);
            }
        });

        app.MapGet("/api/chat/history", async (IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new GetChatHistoryRequest(DefaultSessionId), ct);
            var messages = response.Messages.Select(ToDto).ToList();
            return Results.Ok(new ChatHistoryDto(messages));
        });

        return app;
    }

    private static MessageDto ToDto(Message m) => new(
        m.Id,
        m.SessionId,
        m.Role.ToString().ToLowerInvariant(),
        m.Content,
        m.ProposalsJson,
        m.Incomplete,
        m.CreatedAt);

    public sealed record SendMessagePayload(string Message);

    private sealed record ChatHistoryDto(IReadOnlyList<MessageDto> Messages);

    private sealed record MessageDto(
        long Id,
        string SessionId,
        string Role,
        string Content,
        string? ProposalsJson,
        bool Incomplete,
        DateTimeOffset CreatedAt);
}
