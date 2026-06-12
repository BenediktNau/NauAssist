namespace NauAssist.Backend.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Text("ok")).AllowAnonymous();
        return app;
    }
}
