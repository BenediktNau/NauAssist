using NauAssist.Backend.Features.Calendar.Google;

namespace NauAssist.Backend.Endpoints;

public static class CalendarAuthEndpoints
{
    public static IEndpointRouteBuilder MapCalendarAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/calendar/auth/start", async (
            GoogleAuthService auth,
            AuthSessionStore sessions,
            CancellationToken ct) =>
        {
            try
            {
                var (url, flow) = await auth.StartAuthorizationAsync(ct);
                var id = sessions.Put(flow);
                return Results.Ok(new { authUrl = url, sessionId = id });
            }
            catch (NotAuthenticatedException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/calendar/auth/complete", async (
            CompleteAuthPayload payload,
            AuthSessionStore sessions,
            GoogleAuthService auth,
            CancellationToken ct) =>
        {
            var flow = sessions.Take(payload.SessionId);
            if (flow is null)
            {
                return Results.StatusCode(410);
            }
            try
            {
                await auth.ExchangeCodeAsync(flow, payload.Code, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Code ungültig: {ex.Message}" });
            }
        });

        app.MapPost("/api/calendar/auth/disconnect", async (GoogleAuthService auth) =>
        {
            await auth.DisconnectAsync();
            return Results.Ok(new { ok = true });
        });

        return app;
    }

    public sealed record CompleteAuthPayload(string SessionId, string Code);
}
