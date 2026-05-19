using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;
using NauAssist.Backend.Features.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.AddSingleton<AppDb>();
builder.Services.AddSingleton<DbInitializer>();

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

// Migrationen beim Startup ausführen
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    initializer.Initialize();
}

app.MapHealthEndpoints();

app.Run();

// Für WebApplicationFactory<Program>
public partial class Program;
