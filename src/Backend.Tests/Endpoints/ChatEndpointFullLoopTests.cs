using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class ChatEndpointFullLoopTests : IDisposable
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
    public async Task FullLoop_RequestProposeAndConfirm_CreatesAuditedEvent()
    {
        // Turn 1: User fragt nach Termin
        //   LLM ruft lookup_free_slots → present_proposals → schickt Text
        _fakeLlm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            "c1", "lookup_free_slots",
            JsonDocument.Parse("""{"from":"2026-05-20T08:00:00+00:00","to":"2026-05-22T17:00:00+00:00","duration_minutes":60}""").RootElement)));
        _fakeLlm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            "c2", "present_proposals",
            JsonDocument.Parse("""{"slots":[{"start":"2026-05-20T09:00:00+00:00","end":"2026-05-20T10:00:00+00:00"}]}""").RootElement)));
        _fakeLlm.QueueResponse(new TextDeltaChunk("Wie wäre Mi 20.05. 09:00?"));

        using var app = Build();
        using var client = app.CreateClient();

        using var resp1 = await client.PostAsJsonAsync("/api/chat", new { message = "Treffen mit Anna nächste Woche?" });
        resp1.EnsureSuccessStatusCode();
        var events1 = await SseTestConsumer.ConsumeAsync(
            await resp1.Content.ReadAsStreamAsync(), CancellationToken.None);

        events1.Select(e => e.Event).Should()
            .Contain("tool_started")
            .And.Contain("tool_finished")
            .And.Contain("proposals")
            .And.Contain("done");

        // Turn 2: User bestätigt
        //   LLM ruft create_event → schickt Bestätigungstext
        _fakeLlm.QueueResponse(new ToolCallChunk(new LlmToolCall(
            "c3", "create_event",
            JsonDocument.Parse("""{"title":"Treffen mit Anna","start":"2026-05-20T09:00:00+00:00","end":"2026-05-20T10:00:00+00:00"}""").RootElement)));
        _fakeLlm.QueueResponse(new TextDeltaChunk("Erledigt!"));

        using var resp2 = await client.PostAsJsonAsync("/api/chat", new { message = "Ja, passt." });
        resp2.EnsureSuccessStatusCode();
        var events2 = await SseTestConsumer.ConsumeAsync(
            await resp2.Content.ReadAsStreamAsync(), CancellationToken.None);

        events2.Select(e => e.Event).Should().Contain("tool_finished").And.Contain("done");

        // Kalender hat den Termin
        _fakeCal.CreatedEvents.Should().HaveCount(1);
        _fakeCal.CreatedEvents[0].Title.Should().Be("Treffen mit Anna");

        // Audit-Log enthält create_event
        using var scope = app.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<NauAssist.Backend.Features.Infrastructure.Audit.AuditLogRepository>();
        (await audit.CountAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);

        // History enthält 4 Nachrichten (2x user, 2x assistant)
        using var historyResp = await client.GetAsync("/api/chat/history");
        historyResp.EnsureSuccessStatusCode();
        var history = await historyResp.Content.ReadFromJsonAsync<HistoryDto>();
        history!.Messages.Should().HaveCount(4);
        history.Messages.Select(m => m.Role).Should().Equal("user", "assistant", "user", "assistant");
        history.Messages[1].ProposalsJson.Should().NotBeNull();
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
