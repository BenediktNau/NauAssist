using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.Settings.UpdateCalendarSettings;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class UpdateCalendarSettingsHandlerTests
{
    [Fact]
    public async Task Handle_InvalidWorkingHoursStart_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "25:00", "18:00", 60, 14, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("WorkingHoursStart");
    }

    [Fact]
    public async Task Handle_EndBeforeStart_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "18:00", "09:00", 60, 14, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("Ende");
    }

    [Fact]
    public async Task Handle_NegativeSearchHorizon_ReturnsError()
    {
        var handler = new UpdateCalendarSettingsHandler(new InMemoryRepo());

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 0, null, null),
            CancellationToken.None);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("SearchHorizon");
    }

    [Fact]
    public async Task Handle_ValidUpdate_PersistsCalendar()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "work@nau.studio", "07:30", "19:00", 45, 21, null, null),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Calendar.CalendarId.Should().Be("work@nau.studio");
        repo.Calendar.WorkingHoursStart.Should().Be(new TimeOnly(7, 30));
        repo.Calendar.WorkingHoursEnd.Should().Be(new TimeOnly(19, 0));
        repo.Calendar.DefaultDurationMinutes.Should().Be(45);
        repo.Calendar.SearchHorizonDays.Should().Be(21);
    }

    [Fact]
    public async Task Handle_BothCredentialFieldsProvided_PersistsCredentials()
    {
        var repo = new InMemoryRepo();
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 14,
            "id.apps.googleusercontent.com", "GOCSPX-abc"),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Credentials.Should().NotBeNull();
        repo.Credentials!.ClientId.Should().Be("id.apps.googleusercontent.com");
        repo.Credentials.ClientSecret.Should().Be("GOCSPX-abc");
    }

    [Fact]
    public async Task Handle_EmptyClientSecret_ClearsCredentials()
    {
        var repo = new InMemoryRepo();
        await repo.SetGoogleCredentialsAsync(
            new GoogleCredentials("old-id", "old-secret"), CancellationToken.None);
        var handler = new UpdateCalendarSettingsHandler(repo);

        var r = await handler.Handle(new UpdateCalendarSettingsRequest(
            "primary", "09:00", "18:00", 60, 14, null, ""),
            CancellationToken.None);

        r.Ok.Should().BeTrue();
        repo.Credentials.Should().BeNull();
    }

    private sealed class InMemoryRepo : IAppSettingsRepository
    {
        public CalendarUserSettings Calendar { get; private set; } =
            new("primary", new(9, 0), new(18, 0), 60, 14);
        public GoogleCredentials? Credentials { get; private set; }

        public Task<CalendarUserSettings> GetCalendarAsync(CancellationToken ct) =>
            Task.FromResult(Calendar);
        public Task SetCalendarAsync(CalendarUserSettings s, CancellationToken ct)
        {
            Calendar = s; return Task.CompletedTask;
        }
        public Task<GoogleCredentials?> GetGoogleCredentialsAsync(CancellationToken ct) =>
            Task.FromResult(Credentials);
        public Task SetGoogleCredentialsAsync(GoogleCredentials c, CancellationToken ct)
        {
            Credentials = string.IsNullOrEmpty(c.ClientId) ? null : c;
            return Task.CompletedTask;
        }

        public Task<LlmSettings> GetLlmAsync(CancellationToken ct) =>
            Task.FromResult(new LlmSettings("gemma4:26b"));
        public Task SetLlmAsync(LlmSettings s, CancellationToken ct) => Task.CompletedTask;
        public Task<OllamaUserSettings> GetOllamaAsync(CancellationToken ct) =>
            Task.FromResult(new OllamaUserSettings("http://localhost:11434", null, 16384, 0.3));
        public Task SetOllamaAsync(OllamaUserSettings s, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
