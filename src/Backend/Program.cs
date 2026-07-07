using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Agent.Tools;
using NauAssist.Backend.Features.AutonomousAgent;
using NauAssist.Backend.Features.AutonomousAgent.Classification;
using NauAssist.Backend.Features.AutonomousAgent.Push;
using NauAssist.Backend.Features.AutonomousAgent.Sources;
using NauAssist.Backend.Features.AutonomousAgent.Sources.Imap;
using NauAssist.Backend.Features.AutonomousAgent.Sources.WhatsApp;
using NauAssist.Backend.Features.Calendar;
using NauAssist.Backend.Features.Calendar.CalendarContext;
using NauAssist.Backend.Features.Calendar.Google;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.Infrastructure.Audit;
using NauAssist.Backend.Features.Infrastructure.Llm;
using NauAssist.Backend.Features.Infrastructure.Llm.Ollama;
using NauAssist.Backend.Features.Infrastructure.Persistence;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Features.WatchJobs.Tools;
using NauAssist.Backend.Features.Web;
using NauAssist.Backend.Features.Web.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<AutonomousAgentOptions>(builder.Configuration.GetSection("AutonomousAgent"));
builder.Services.Configure<TimeOptions>(builder.Configuration.GetSection("Time"));

builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

// User-Kontext: scoped, beide Interfaces auf derselben Instanz. Default = Single-User;
// gesetzt wird er von der Auth-Middleware (HTTP) bzw. vom Scheduler (Background).
builder.Services.AddScoped<UserContextHolder>();
builder.Services.AddScoped<IUserContext>(sp => sp.GetRequiredService<UserContextHolder>());
builder.Services.AddScoped<IUserContextSetter>(sp => sp.GetRequiredService<UserContextHolder>());
builder.Services.AddScoped<UserRepository>();
builder.Services.AddHttpContextAccessor();

// Keycloak-Auth (BFF) ist opt-in — aus heißt: exakt heutiges Single-User-Verhalten.
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
if (authOptions.Enabled)
{
    builder.AddBffAuth(authOptions);
}

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
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<AuthSessionStore>();
builder.Services.AddScoped<GoogleAuthService>();
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
builder.Services.AddHttpClient("Ollama");
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
builder.Services.AddScoped<ITool, DeleteEventTool>();
builder.Services.AddScoped<ITool, UpdateEventTool>();
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

// Autonomer Agent — Scheduler als Singleton (manueller Trigger + BackgroundService teilen Instanz)
builder.Services.AddScoped<SuggestionRepository>();
builder.Services.AddScoped<SourceAccountRepository>();
builder.Services.AddScoped<SourceCursorRepository>();
builder.Services.AddScoped<ImapClient>();
builder.Services.AddScoped<ISourceObserver, ImapObserver>();
builder.Services.AddScoped<ISourceSender, SmtpSender>();

// WhatsApp (opt-in): nur registrieren, wenn aktiviert. Options werden immer gebunden,
// damit der Capabilities-Endpoint den Enabled-Status lesen kann.
builder.Services.Configure<WhatsAppOptions>(
    builder.Configuration.GetSection("AutonomousAgent:WhatsApp"));
var whatsAppOptions = builder.Configuration
    .GetSection("AutonomousAgent:WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();
if (whatsAppOptions.Enabled)
{
    builder.Services.AddHttpClient("WhatsApp", client =>
    {
        client.BaseAddress = new Uri(whatsAppOptions.SidecarBaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrEmpty(whatsAppOptions.SharedSecret))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", whatsAppOptions.SharedSecret);
        }
    });
    builder.Services.AddScoped<IWhatsAppSidecarClient, WhatsAppSidecarClient>();
    builder.Services.AddScoped<ISourceObserver, WhatsAppObserver>();
    builder.Services.AddScoped<ISourceSender, WhatsAppSender>();
}

builder.Services.AddScoped<IntentClassifier>();
builder.Services.AddScoped<DraftReplyGenerator>();
builder.Services.AddScoped<AutonomousReasoner>();
builder.Services.AddScoped<PushSubscriptionRepository>();
builder.Services.AddScoped<WebPushSender>();
builder.Services.AddScoped<VapidBootstrapper>();
builder.Services.AddSingleton<AutonomousAgentScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutonomousAgentScheduler>());

// Watch-Jobs (opt-in). Options + Bausteine werden immer registriert (Capabilities/Endpoint-Gating,
// einfache Tests); Scheduler und Chat-Tools nur, wenn das Feature aktiv ist.
builder.Services.Configure<WatchJobOptions>(
    builder.Configuration.GetSection("AutonomousAgent:WatchJobs"));
builder.Services.Configure<WebOptions>(
    builder.Configuration.GetSection("Web"));
var watchJobOptions = builder.Configuration
    .GetSection("AutonomousAgent:WatchJobs").Get<WatchJobOptions>() ?? new WatchJobOptions();

builder.Services.AddHttpClient(SearxngWebSearch.HttpClientName);
// Fetch beliebiger Ziel-URLs läuft über einen SSRF-gehärteten Client (blockt interne/
// private Adressen, auch über Redirects). SearXNG bleibt bewusst ungeschützt = interner Dienst.
builder.Services.AddHttpClient(HttpWebFetch.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(SsrfGuard.CreateGuardedHandler);
builder.Services.AddScoped<IWebSearch, SearxngWebSearch>();
builder.Services.AddScoped<IWebFetch, HttpWebFetch>();

var webOptions = builder.Configuration.GetSection("Web").Get<WebOptions>() ?? new WebOptions();

// Web-Chat-Tools nur anbieten, wenn eine SearXNG-Instanz konfiguriert ist — ohne
// Such-Backend wären web_search/fetch_webpage tote Tools im Prompt.
if (!string.IsNullOrWhiteSpace(webOptions.SearxngBaseUrl))
{
    builder.Services.AddScoped<ITool, WebSearchTool>();
    builder.Services.AddScoped<ITool, FetchWebpageTool>();
}

builder.Services.AddScoped<WatchJobRepository>();
builder.Services.AddScoped<WatchJudge>();
builder.Services.AddScoped<WatchJobExecutor>();
builder.Services.AddScoped<WatchJobNotifier>();

if (watchJobOptions.Enabled)
{
    builder.Services.AddScoped<ITool, CreateWatchJobTool>();
    builder.Services.AddScoped<ITool, ListWatchJobsTool>();
    builder.Services.AddScoped<ITool, CancelWatchJobTool>();
    builder.Services.AddSingleton<WatchJobScheduler>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<WatchJobScheduler>());
}

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

    var vapid = scope.ServiceProvider.GetRequiredService<VapidBootstrapper>();
    await vapid.EnsureKeysAsync(CancellationToken.None);
}

// Sub-Command "auth"
if (args.Contains("auth"))
{
    return await GoogleAuthCommand.RunAsync(app.Services, CancellationToken.None);
}

app.UseDefaultFiles();
app.UseStaticFiles();

if (authOptions.Enabled)
{
    app.UseBffAuth();
    app.MapAuthEndpoints();
}

app.MapHealthEndpoints();
app.MapRulesEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();
app.MapCalendarAuthEndpoints();
app.MapCalendarEndpoints();
app.MapSuggestionsEndpoints();
app.MapSourceAccountsEndpoints();
app.MapPushEndpoints();
app.MapCapabilitiesEndpoints();
if (whatsAppOptions.Enabled)
{
    app.MapWhatsAppSourceEndpoints();
}
if (watchJobOptions.Enabled)
{
    app.MapWatchJobsEndpoints();
}

// Frontend muss vor dem Login laden — es stößt den Redirect zu Keycloak selbst an.
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();
return 0;

// Für WebApplicationFactory<Program>
public partial class Program;
