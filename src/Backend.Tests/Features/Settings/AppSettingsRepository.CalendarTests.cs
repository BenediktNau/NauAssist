using FluentAssertions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.Settings;

public sealed class AppSettingsRepositoryCalendarTests
{
    [Fact]
    public async Task GetCalendar_ReturnsSeededDefaults()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        var s = await repo.GetCalendarAsync(CancellationToken.None);

        s.CalendarId.Should().Be("primary");
        s.WorkingHoursStart.Should().Be(new TimeOnly(9, 0));
        s.WorkingHoursEnd.Should().Be(new TimeOnly(18, 0));
        s.DefaultDurationMinutes.Should().Be(60);
        s.SearchHorizonDays.Should().Be(14);
    }

    [Fact]
    public async Task SetCalendar_RoundtripsAllFields()
    {
        using var db = new TempSqliteDb();
        var repo = new AppSettingsRepository(db.AppDb);

        await repo.SetCalendarAsync(
            new CalendarUserSettings(
                CalendarId: "work@nau.studio",
                WorkingHoursStart: new TimeOnly(7, 30),
                WorkingHoursEnd: new TimeOnly(19, 45),
                DefaultDurationMinutes: 30,
                SearchHorizonDays: 21),
            CancellationToken.None);

        var loaded = await repo.GetCalendarAsync(CancellationToken.None);

        loaded.CalendarId.Should().Be("work@nau.studio");
        loaded.WorkingHoursStart.Should().Be(new TimeOnly(7, 30));
        loaded.WorkingHoursEnd.Should().Be(new TimeOnly(19, 45));
        loaded.DefaultDurationMinutes.Should().Be(30);
        loaded.SearchHorizonDays.Should().Be(21);
    }
}
