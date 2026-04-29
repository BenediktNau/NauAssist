using NauAssist.Common.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddNauAssistConfiguration(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "NauAssist läuft.");

app.Run();

public partial class Program;
