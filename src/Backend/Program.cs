using NauAssist.Backend.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

// Für WebApplicationFactory<Program>
public partial class Program;
