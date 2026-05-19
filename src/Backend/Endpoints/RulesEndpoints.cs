using Mediator;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Rules.AddRule;
using NauAssist.Backend.Features.Rules.DeleteRule;
using NauAssist.Backend.Features.Rules.ListRules;

namespace NauAssist.Backend.Endpoints;

public static class RulesEndpoints
{
    public static IEndpointRouteBuilder MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rules");

        group.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new ListRulesRequest(), ct);
            return Results.Ok(response.Rules.Select(ToDto));
        });

        group.MapPost("/", async (AddRuleDto body, IMediator mediator, CancellationToken ct) =>
        {
            try
            {
                var request = new AddRuleRequest(
                    Text: body.Text,
                    DaysOfWeek: (DayOfWeekFlags)body.DaysOfWeek,
                    TimeRangeStart: ParseTime(body.TimeRangeStart),
                    TimeRangeEnd: ParseTime(body.TimeRangeEnd),
                    Hardness: Enum.Parse<RuleHardness>(body.Hardness, ignoreCase: true));

                var response = await mediator.Send(request, ct);
                return Results.Created($"/api/rules/{response.Rule.Id}", ToDto(response.Rule));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:long}", async (long id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DeleteRuleRequest(id), ct);
            return response.Deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static TimeOnly? ParseTime(string? raw) =>
        string.IsNullOrEmpty(raw) ? null : TimeOnly.Parse(raw);

    private static RuleDto ToDto(Rule r) => new(
        r.Id,
        r.Text,
        (int)r.DaysOfWeek,
        r.TimeRangeStart?.ToString("HH:mm"),
        r.TimeRangeEnd?.ToString("HH:mm"),
        r.Hardness.ToString().ToLowerInvariant(),
        r.CreatedAt);

    private sealed record AddRuleDto(
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness);

    private sealed record RuleDto(
        long Id,
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness,
        DateTimeOffset CreatedAt);
}
