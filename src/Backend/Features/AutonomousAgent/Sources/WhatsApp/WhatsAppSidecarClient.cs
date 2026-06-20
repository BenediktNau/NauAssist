using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;

/// <summary>
/// Dünner HTTP-Wrapper für den WhatsApp-Sidecar. BaseAddress + Bearer-Auth werden
/// am named HttpClient "WhatsApp" zentral gesetzt (siehe Program.cs).
/// </summary>
public sealed class WhatsAppSidecarClient : IWhatsAppSidecarClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _http;

    public WhatsAppSidecarClient(IHttpClientFactory http)
    {
        _http = http;
    }

    public async Task<WhatsAppSession> CreateSessionAsync(string? sessionId, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.PostAsJsonAsync("sessions", new { sessionId }, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<WhatsAppSession>(JsonOpts, ct))
               ?? throw new InvalidOperationException("Sidecar lieferte leere Session-Antwort.");
    }

    public async Task<WhatsAppSessionStatus?> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.GetAsync($"sessions/{Uri.EscapeDataString(sessionId)}", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<WhatsAppSessionStatus>(JsonOpts, ct);
    }

    public async Task<IReadOnlyList<WhatsAppChat>> ListChatsAsync(string sessionId, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.GetAsync($"sessions/{Uri.EscapeDataString(sessionId)}/chats", ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return Array.Empty<WhatsAppChat>();
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<WhatsAppChat>>(JsonOpts, ct)
               ?? (IReadOnlyList<WhatsAppChat>)Array.Empty<WhatsAppChat>();
    }

    public async Task<WhatsAppMessagePage> GetMessagesAsync(string sessionId, long since, int limit, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        var url = $"sessions/{Uri.EscapeDataString(sessionId)}/messages?since={since}&limit={limit}";
        using var res = await client.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<WhatsAppMessagePage>(JsonOpts, ct))
               ?? new WhatsAppMessagePage(Array.Empty<WhatsAppMessage>(), since);
    }

    public async Task SendAsync(string sessionId, string chatId, string text, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.PostAsJsonAsync(
            $"sessions/{Uri.EscapeDataString(sessionId)}/send",
            new { chatId, text },
            ct);
        res.EnsureSuccessStatusCode();
    }

    public async Task<WhatsAppResolveResult> ResolveChatAsync(string sessionId, string phone, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.PostAsJsonAsync(
            $"sessions/{Uri.EscapeDataString(sessionId)}/resolve",
            new { phone },
            ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<WhatsAppResolveResult>(JsonOpts, ct))
               ?? new WhatsAppResolveResult("", null, false);
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        using var client = _http.CreateClient("WhatsApp");
        using var res = await client.DeleteAsync($"sessions/{Uri.EscapeDataString(sessionId)}", ct);
        if (res.StatusCode != HttpStatusCode.NotFound)
        {
            res.EnsureSuccessStatusCode();
        }
    }
}
