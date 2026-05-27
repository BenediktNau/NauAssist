using Microsoft.AspNetCore.Http;
using NauAssist.Backend.Features.AutonomousAgent.Push;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Endpoints;

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push");

        group.MapGet("/vapid-public-key", async (
            IAppSettingsRepository settings,
            CancellationToken ct) =>
        {
            var vapid = await settings.GetVapidAsync(ct);
            return Results.Ok(new VapidPublicKeyDto(vapid.PublicKey, vapid.IsConfigured));
        });

        group.MapPost("/subscribe", async (
            SubscribePayload body,
            HttpContext http,
            PushSubscriptionRepository repo,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Endpoint)
                || string.IsNullOrWhiteSpace(body.Keys.P256dh)
                || string.IsNullOrWhiteSpace(body.Keys.Auth))
            {
                return Results.BadRequest(new { error = "subscription_incomplete" });
            }

            var ua = http.Request.Headers.UserAgent.ToString();
            var sub = await repo.UpsertAsync(
                body.Endpoint, body.Keys.P256dh, body.Keys.Auth,
                string.IsNullOrEmpty(ua) ? null : ua,
                clock(), ct);
            return Results.Ok(new { id = sub.Id });
        });

        group.MapDelete("/subscribe", async (
            string endpoint,
            PushSubscriptionRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return Results.BadRequest(new { error = "endpoint_required" });
            }
            var ok = await repo.DeleteByEndpointAsync(endpoint, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/test", async (
            WebPushSender sender,
            CancellationToken ct) =>
        {
            var sent = await sender.BroadcastAsync(
                new PushNotificationPayload(
                    Title: "NauAssist · Test",
                    Body: "Push funktioniert.",
                    Url: "/",
                    Tag: "test"),
                ct);
            return Results.Ok(new { sent });
        });

        return app;
    }

    private sealed record SubscribePayload(string Endpoint, SubscriptionKeys Keys);
    private sealed record SubscriptionKeys(string P256dh, string Auth);
    private sealed record VapidPublicKeyDto(string PublicKey, bool Configured);
}
