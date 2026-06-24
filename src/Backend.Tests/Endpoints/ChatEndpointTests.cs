using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class ChatEndpointTests : IDisposable
{
    private readonly TestAppFactory _factory = new();
    private readonly FakeLlmClient _fakeLlm = new();
    private readonly FakeCalendarProvider _fakeCal = new();

    private WebApplicationFactory<Program> Build() =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(svc =>
        {
            svc.AddSingleton<ILlmClient>(_fakeLlm);
            svc.AddSingleton<ICalendarProvider>(_fakeCal);
        }));

    [Fact]
    public async Task PostChat_ResponseIsTextEventStream()
    {
        _fakeLlm.QueueResponse(new TextDeltaChunk("Hallo"));

        using var client = Build().CreateClient();
        using var response = await client.PostAsJsonAsync("/api/chat", new { message = "Hi" });

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task PostChat_StreamsTokensFollowedByDone()
    {
        _fakeLlm.QueueResponse(new TextDeltaChunk("Hallo "), new TextDeltaChunk("Welt"));

        using var client = Build().CreateClient();
        using var response = await client.PostAsJsonAsync("/api/chat", new { message = "Hi" });
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        var events = await SseTestConsumer.ConsumeAsync(stream, CancellationToken.None);

        events.Select(e => e.Event).Should().ContainInOrder("token", "token", "done");

        var firstToken = JsonSerializer.Deserialize<JsonElement>(events[0].Data);
        firstToken.GetProperty("text").GetString().Should().Be("Hallo ");
    }

    [Fact]
    public async Task GetHistory_ReturnsPersistedMessages()
    {
        _fakeLlm.QueueResponse(new TextDeltaChunk("Hi back."));

        using var client = Build().CreateClient();

        using var postResponse = await client.PostAsJsonAsync("/api/chat", new { message = "Hi" });
        postResponse.EnsureSuccessStatusCode();
        await postResponse.Content.ReadAsStringAsync(); // SSE-Stream zu Ende lesen

        using var historyResponse = await client.GetAsync("/api/chat/history");
        historyResponse.EnsureSuccessStatusCode();
        var history = await historyResponse.Content.ReadFromJsonAsync<HistoryDto>();

        history!.Messages.Should().HaveCount(2);
        history.Messages[0].Role.Should().Be("user");
        history.Messages[0].Content.Should().Be("Hi");
        history.Messages[1].Role.Should().Be("assistant");
        history.Messages[1].Content.Should().Be("Hi back.");
    }

    public void Dispose() => _factory.Dispose();

    private sealed record HistoryDto(List<MessageDto> Messages);

    private sealed record MessageDto(
        long Id,
        string SessionId,
        string Role,
        string Content,
        string? ProposalsJson,
        bool Incomplete,
        DateTimeOffset CreatedAt);
}
