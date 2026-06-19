using System.Text.Json;
using NauAssist.Backend.Features.AutonomousAgent.Sources;
using NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

namespace NauAssist.Backend.Endpoints;

public static class SourceAccountsEndpoints
{
    public static IEndpointRouteBuilder MapSourceAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/source-accounts");

        group.MapGet("/", async (string? kind, SourceAccountRepository repo, CancellationToken ct) =>
        {
            var items = await repo.ListAsync(kind, ct);
            return Results.Ok(items.Select(ToDto));
        });

        group.MapGet("/{id:long}", async (long id, SourceAccountRepository repo, CancellationToken ct) =>
        {
            var a = await repo.GetAsync(id, ct);
            return a is null ? Results.NotFound() : Results.Ok(ToDto(a));
        });

        group.MapPost("/", async (
            CreateAccountPayload body,
            SourceAccountRepository repo,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Kind) || string.IsNullOrWhiteSpace(body.DisplayName))
            {
                return Results.BadRequest(new { error = "kind_and_display_name_required" });
            }

            try
            {
                // Kind-spezifische Validierung
                if (body.Kind == ImapObserver.SourceKey)
                {
                    _ = ImapCredentials.Parse(JsonSerializer.Serialize(body.Credentials));
                }
                else if (body.Kind == WhatsAppObserver.SourceKey)
                {
                    _ = WhatsAppCredentials.Parse(JsonSerializer.Serialize(body.Credentials));
                }
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var credentialsJson = JsonSerializer.Serialize(body.Credentials);
            var allowlist = body.Allowlist ?? Array.Empty<string>();
            var created = await repo.AddAsync(body.Kind, body.DisplayName, credentialsJson, allowlist, clock(), ct);
            return Results.Created($"/api/source-accounts/{created.Id}", ToDto(created));
        });

        group.MapPatch("/{id:long}", async (
            long id,
            UpdateAccountPayload body,
            SourceAccountRepository repo,
            Func<DateTimeOffset> clock,
            CancellationToken ct) =>
        {
            var existing = await repo.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();

            string? credentialsJson = body.Credentials is null
                ? null
                : JsonSerializer.Serialize(body.Credentials);

            await repo.UpdateAsync(
                id,
                body.DisplayName,
                credentialsJson,
                body.Allowlist,
                body.Enabled,
                clock(),
                ct);

            var updated = await repo.GetAsync(id, ct);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:long}", async (long id, SourceAccountRepository repo, CancellationToken ct) =>
        {
            var ok = await repo.DeleteAsync(id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // IMAP-Folder listen (inline Credentials beim Anlegen).
        group.MapPost("/imap/list-folders", async (
            ListImapFoldersPayload body,
            ImapClient client,
            CancellationToken ct) => await ListImapFoldersAsync(
                () => Task.FromResult(JsonSerializer.Serialize(body.Credentials)),
                client,
                ct));

        // IMAP-Folder neu laden für einen bereits gespeicherten Account.
        group.MapGet("/{id:long}/imap/folders", async (
            long id,
            SourceAccountRepository repo,
            ImapClient client,
            CancellationToken ct) =>
        {
            var account = await repo.GetAsync(id, ct);
            if (account is null) return Results.NotFound();
            if (account.Kind != ImapObserver.SourceKey)
            {
                return Results.BadRequest(new { error = "account_not_imap" });
            }
            return await ListImapFoldersAsync(() => Task.FromResult(account.CredentialsJson), client, ct);
        });

        return app;
    }

    /// <summary>
    /// WhatsApp-spezifische Helfer (QR-Pairing-Flow + Chat-Listing). Wird nur gemappt,
    /// wenn <c>AutonomousAgent:WhatsApp:Enabled</c> — dann ist auch der Sidecar-Client registriert.
    /// </summary>
    public static IEndpointRouteBuilder MapWhatsAppSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/source-accounts");

        // Neue Session starten (oder bestehende renutzen) → liefert sessionId + state.
        group.MapPost("/whatsapp/start", async (
            StartWhatsAppPayload? body,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await client.CreateSessionAsync(body?.SessionId, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        // Pairing-Status pollen (QR-Data-URL + Telefonnummer sobald verbunden).
        group.MapGet("/whatsapp/session/{sessionId}", async (
            string sessionId,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            try
            {
                var status = await client.GetSessionAsync(sessionId, ct);
                return status is null ? Results.NotFound() : Results.Ok(status);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        // Chats für die Allowlist-Auswahl (inline, während des Anlegens).
        group.MapGet("/whatsapp/session/{sessionId}/chats", async (
            string sessionId,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await client.ListChatsAsync(sessionId, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        // Telefonnummer → kanonische JID (+ LID) für die manuelle Chat-Auswahl.
        group.MapPost("/whatsapp/session/{sessionId}/resolve", async (
            string sessionId,
            ResolveChatPayload? body,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Phone))
            {
                return Results.BadRequest(new { error = "phone_required" });
            }
            try
            {
                return Results.Ok(await client.ResolveChatAsync(sessionId, body.Phone, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        // Chats für einen bereits gespeicherten Account neu laden.
        group.MapGet("/{id:long}/whatsapp/chats", async (
            long id,
            SourceAccountRepository repo,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            var account = await repo.GetAsync(id, ct);
            if (account is null) return Results.NotFound();
            if (account.Kind != WhatsAppObserver.SourceKey)
            {
                return Results.BadRequest(new { error = "account_not_whatsapp" });
            }
            try
            {
                var creds = WhatsAppCredentials.Parse(account.CredentialsJson);
                return Results.Ok(await client.ListChatsAsync(creds.SessionId, ct));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        // Sidecar-Session beenden (Logout + Auth-State löschen). Vom UI beim Account-Löschen.
        group.MapDelete("/whatsapp/session/{sessionId}", async (
            string sessionId,
            IWhatsAppSidecarClient client,
            CancellationToken ct) =>
        {
            try
            {
                await client.DeleteSessionAsync(sessionId, ct);
                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = "sidecar_unreachable", detail = ex.Message });
            }
        });

        return app;
    }

    private static async Task<IResult> ListImapFoldersAsync(
        Func<Task<string>> credentialsJsonProvider,
        ImapClient client,
        CancellationToken ct)
    {
        try
        {
            var credentialsJson = await credentialsJsonProvider();
            var creds = ImapCredentials.Parse(credentialsJson);
            var folders = await client.ListFoldersAsync(creds, ct);
            return Results.Ok(folders);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = "imap_request_failed", detail = ex.Message });
        }
    }

    private static SourceAccountDto ToDto(SourceAccount a)
    {
        var credentialsRedacted = RedactCredentials(a.Kind, a.CredentialsJson);
        return new SourceAccountDto(
            a.Id,
            a.Kind,
            a.DisplayName,
            credentialsRedacted,
            a.Allowlist,
            a.Enabled,
            a.CreatedAt,
            a.UpdatedAt);
    }

    private static Dictionary<string, string?> RedactCredentials(string kind, string credentialsJson)
    {
        // Für Sichtbarkeit im UI ohne Geheimnisse: nur unkritische Felder durchlassen.
        var result = new Dictionary<string, string?>();
        try
        {
            using var doc = JsonDocument.Parse(credentialsJson);
            if (kind == ImapObserver.SourceKey)
            {
                if (doc.RootElement.TryGetProperty("imapHost", out var ih))
                    result["imapHost"] = ih.GetString();
                if (doc.RootElement.TryGetProperty("smtpHost", out var sh))
                    result["smtpHost"] = sh.GetString();
                if (doc.RootElement.TryGetProperty("username", out var un))
                    result["username"] = un.GetString();
                result["password"] = "***";
            }
            else if (kind == WhatsAppObserver.SourceKey)
            {
                // Keine Geheimnisse in den Credentials — Auth-State liegt im Sidecar.
                if (doc.RootElement.TryGetProperty("sessionId", out var sid))
                    result["sessionId"] = sid.GetString();
                if (doc.RootElement.TryGetProperty("phoneLabel", out var pl))
                    result["phoneLabel"] = pl.GetString();
            }
        }
        catch (JsonException)
        {
            // Defekte Credentials → leeres Mapping. UI zeigt Account trotzdem an.
        }
        return result;
    }

    private sealed record CreateAccountPayload(
        string Kind,
        string DisplayName,
        Dictionary<string, object> Credentials,
        IReadOnlyList<string>? Allowlist);

    private sealed record UpdateAccountPayload(
        string? DisplayName,
        Dictionary<string, object>? Credentials,
        IReadOnlyList<string>? Allowlist,
        bool? Enabled);

    private sealed record ListImapFoldersPayload(Dictionary<string, object> Credentials);
    private sealed record StartWhatsAppPayload(string? SessionId);
    private sealed record ResolveChatPayload(string? Phone);

    private sealed record SourceAccountDto(
        long Id,
        string Kind,
        string DisplayName,
        Dictionary<string, string?> Credentials,
        IReadOnlyList<string> Allowlist,
        bool Enabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
