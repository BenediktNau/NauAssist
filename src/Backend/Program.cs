using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Infrastructure.Persistence;
using NauAssist.Backend.Features.Rules;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<CalendarOptions>(builder.Configuration.GetSection("Calendar"));

builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

builder.Services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
builder.Services.AddScoped<RuleRepository>();
builder.Services.AddSingleton(_ =>
    new RuleApplicator(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));

// Calendar
builder.Services.AddSingleton<SqliteDataStore>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CalendarOptions>>().Value;
    return new FreeSlotCalculator(
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"),
        TimeOnly.Parse(opts.WorkingHoursStart),
        TimeOnly.Parse(opts.WorkingHoursEnd),
        DayOfWeekFlags.WeekdaysOnly);
});

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

// Migrationen beim Startup
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    initializer.Initialize();
}

// Sub-Command "auth": OAuth-Flow ausführen und beenden, ohne den Web-Host zu starten
if (args.Contains("auth"))
{
    return await GoogleAuthCommand.RunAsync(app.Services, CancellationToken.None);
}

app.MapHealthEndpoints();
app.MapRulesEndpoints();

await app.RunAsync();
return 0;

// Für WebApplicationFactory<Program>
public partial class Program;
