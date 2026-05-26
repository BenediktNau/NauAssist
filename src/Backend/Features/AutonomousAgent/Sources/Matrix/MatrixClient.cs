using System.Net.Http.Headers;
using System.Text.Json;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.Matrix;

/// <summary>
/// Dünner HTTP-Wrapper für die Matrix-Client-API. Macht keine Caching-/Retry-Logik
/// — das übernimmt der Observer.
/// </summary>
public sealed class MatrixClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _http;

    public MatrixClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<string>> ListJoinedRoomsAsync(
        MatrixCredentials creds,
        CancellationToken ct)
    {
        using var req = BuildRequest(
            HttpMethod.Get,
            $"{creds.NormalizedHomeserver()}/_matrix/client/v3/joined_rooms",
            creds);
        using var client = _http.CreateClient("Matrix");
        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<JoinedRoomsDto>(stream, JsonOpts, ct);
        return dto?.JoinedRooms ?? Array.Empty<string>();
    }

    public async Task<string?> GetRoomNameAsync(
        MatrixCredentials creds,
        string roomId,
        CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(roomId);
        using var req = BuildRequest(
            HttpMethod.Get,
            $"{creds.NormalizedHomeserver()}/_matrix/client/v3/rooms/{encoded}/state/m.room.name/",
            creds);
        using var client = _http.CreateClient("Matrix");
        using var res = await client.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode) return null;

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        var dto = await JsonSerializer.DeserializeAsync<RoomNameDto>(stream, JsonOpts, ct);
        return dto?.Name;
    }

    public async Task<MatrixSyncResult> SyncAsync(
        MatrixCredentials creds,
        string? since,
        IReadOnlyList<string> allowedRooms,
        CancellationToken ct)
    {
        if (allowedRooms.Count == 0)
        {
            return new MatrixSyncResult(since ?? "", Array.Empty<MatrixMessage>());
        }

        var filter = BuildFilter(allowedRooms);
        var url = $"{creds.NormalizedHomeserver()}/_matrix/client/v3/sync"
                  + $"?filter={Uri.EscapeDataString(filter)}"
                  + "&timeout=0";
        if (!string.IsNullOrEmpty(since))
        {
            url += $"&since={Uri.EscapeDataString(since)}";
        }

        using var req = BuildRequest(HttpMethod.Get, url, creds);
        using var client = _http.CreateClient("Matrix");
        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var nextBatch = root.TryGetProperty("next_batch", out var nb) ? nb.GetString() ?? "" : "";
        var messages = ParseMessages(root, creds.UserId);

        return new MatrixSyncResult(nextBatch, messages);
    }

    private static string BuildFilter(IReadOnlyList<string> allowedRooms)
    {
        // Server-seitige Filterung: nur Timeline-Messages aus erlaubten Räumen.
        var filter = new
        {
            room = new
            {
                rooms = allowedRooms,
                timeline = new { types = new[] { "m.room.message" }, limit = 50 },
                state = new { types = Array.Empty<string>(), lazy_load_members = true },
                ephemeral = new { limit = 0 },
                account_data = new { limit = 0 },
            },
            presence = new { limit = 0 },
            account_data = new { limit = 0 },
        };
        return JsonSerializer.Serialize(filter);
    }

    private static IReadOnlyList<MatrixMessage> ParseMessages(JsonElement root, string ownUserId)
    {
        var messages = new List<MatrixMessage>();
        if (!root.TryGetProperty("rooms", out var rooms)) return messages;
        if (!rooms.TryGetProperty("join", out var join)) return messages;

        foreach (var room in join.EnumerateObject())
        {
            var roomId = room.Name;
            if (!room.Value.TryGetProperty("timeline", out var timeline)) continue;
            if (!timeline.TryGetProperty("events", out var events)) continue;

            foreach (var ev in events.EnumerateArray())
            {
                if (!IsTextMessage(ev)) continue;

                var sender = ev.TryGetProperty("sender", out var s) ? s.GetString() ?? "" : "";
                if (string.Equals(sender, ownUserId, StringComparison.Ordinal))
                {
                    // Eigene Nachrichten ignorieren — sonst loopt der Agent auf sich selbst.
                    continue;
                }

                var eventId = ev.TryGetProperty("event_id", out var eid) ? eid.GetString() ?? "" : "";
                var body = ev.TryGetProperty("content", out var content)
                           && content.TryGetProperty("body", out var bodyEl)
                    ? bodyEl.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(body)) continue;

                var tsMs = ev.TryGetProperty("origin_server_ts", out var ts) ? ts.GetInt64() : 0;
                var timestamp = tsMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(tsMs)
                    : DateTimeOffset.UtcNow;

                messages.Add(new MatrixMessage(roomId, eventId, sender, body, timestamp));
            }
        }
        return messages;
    }

    private static bool IsTextMessage(JsonElement ev)
    {
        if (!ev.TryGetProperty("type", out var t) || t.GetString() != "m.room.message") return false;
        if (!ev.TryGetProperty("content", out var content)) return false;
        var msgType = content.TryGetProperty("msgtype", out var mt) ? mt.GetString() : null;
        return msgType is "m.text" or "m.emote" or "m.notice";
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, MatrixCredentials creds)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.AccessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private sealed record JoinedRoomsDto(IReadOnlyList<string>? JoinedRooms);
    private sealed record RoomNameDto(string? Name);
}
