using NauAssist.Api.Diagnostics;
using NauAssist.Common.Configuration;
using NauAssist.Extensions.Workspace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddNauAssistConfiguration(builder.Configuration);
builder.Services.AddExtensionWorkspace();
builder.Services.AddNauAssistDiagnostics();

builder.Host.UseSerilog((context, services, logger) =>
{
    var paths = services.GetRequiredService<IPathResolver>();
    logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "NauAssist")
        .WriteTo.Console()
        .WriteTo.File(
            formatter: new CompactJsonFormatter(),
            path: Path.Combine(paths.LogsRoot, "nauassist-.log"),
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: 50L * 1024 * 1024,
            retainedFileCountLimit: 14);
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

app.MapNauAssistDiagnostics();
app.MapGet("/", () => "NauAssist läuft.");

app.Run();

public partial class Program;
