using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NauAssist.Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

// Für WebApplicationFactory<Program>
public partial class Program;
