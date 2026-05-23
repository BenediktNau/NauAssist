using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Llm.Gemini;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Infrastructure.Persistence;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<CalendarOptions>(builder.Configuration.GetSection("Calendar"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<TimeOptions>(builder.Configuration.GetSection("Time"));

builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

builder.Services.AddSingleton<Func<DateTimeOffset>>(_ => () => DateTimeOffset.UtcNow);
builder.Services.AddSingleton<TimeZoneInfo>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TimeOptions>>().Value;
    return TimeZoneInfo.FindSystemTimeZoneById(opts.Zone);
});
builder.Services.AddSingleton<ClockContext>(sp =>
{
    var clock = sp.GetRequiredService<Func<DateTimeOffset>>();
    var zone = sp.GetRequiredService<TimeZoneInfo>();
    return new ClockContext(clock, zone);
});
builder.Services.AddScoped<RuleRepository>();
builder.Services.AddSingleton(sp =>
    new RuleApplicator(sp.GetRequiredService<TimeZoneInfo>()));

// Calendar
builder.Services.AddSingleton<SqliteDataStore>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddScoped<ICalendarProvider, GoogleCalendarProvider>();
builder.Services.AddScoped(sp =>
{
    var settings = sp.GetRequiredService<IAppSettingsRepository>();
    var cal = settings.GetCalendarAsync(CancellationToken.None).GetAwaiter().GetResult();
    return new FreeSlotCalculator(
        sp.GetRequiredService<TimeZoneInfo>(),
        cal.WorkingHoursStart,
        cal.WorkingHoursEnd,
        DayOfWeekFlags.WeekdaysOnly);
});

builder.Services.AddScoped<CalendarContextBuilder>();

// LLM
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.AddHttpClient("Ollama");
builder.Services.AddHttpClient("Gemini");
builder.Services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
builder.Services.AddScoped<ILlmClientFactory, LlmClientFactory>();
builder.Services.AddScoped<ILlmClient>(sp =>
{
    var factory = sp.GetRequiredService<ILlmClientFactory>();
    return factory.CreateAsync(CancellationToken.None).GetAwaiter().GetResult();
});

// Agent-Tools (alle Scoped, weil sie IMediator brauchen)
builder.Services.AddScoped<ITool, LookupFreeSlotsTool>();
builder.Services.AddScoped<ITool, CreateEventTool>();
builder.Services.AddScoped<ITool, GetCalendarRangeTool>();
builder.Services.AddScoped<ITool, ListRulesTool>();
builder.Services.AddScoped<ITool, AddRuleTool>();
builder.Services.AddScoped<ITool, DeleteRuleTool>();
builder.Services.AddScoped<ITool, PresentProposalsTool>();
builder.Services.AddScoped<ITool, GetCurrentTimeTool>();
builder.Services.AddScoped<AgentRunner>();

// Chat & Audit
builder.Services.AddScoped<MessageRepository>();
builder.Services.AddScoped<ChatClearMarkerRepository>();
builder.Services.AddScoped<IChatClearMarkerSource>(sp => sp.GetRequiredService<ChatClearMarkerRepository>());
builder.Services.AddScoped<ChatContextCutoff>(sp => new ChatContextCutoff(
    sp.GetRequiredService<IChatClearMarkerSource>(),
    sp.GetRequiredService<Func<DateTimeOffset>>(),
    sp.GetRequiredService<TimeZoneInfo>()));
builder.Services.AddScoped<AuditLogRepository>();

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

// Sub-Command "auth"
if (args.Contains("auth"))
{
    return await GoogleAuthCommand.RunAsync(app.Services, CancellationToken.None);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapRulesEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();

app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

// Für WebApplicationFactory<Program>
public partial class Program;
