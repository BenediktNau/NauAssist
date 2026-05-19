using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class RulesEndpointsTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public RulesEndpointsTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullCycle_PostListDelete_WorksEndToEnd()
    {
        var client = _factory.CreateClient();

        // POST
        var postBody = new
        {
            text = "Mo–Fr nach 18 Uhr nicht",
            daysOfWeek = (int)DayOfWeekFlags.WeekdaysOnly,
            timeRangeStart = "18:00",
            timeRangeEnd = "23:59",
            hardness = "hard",
        };
        var postResp = await client.PostAsJsonAsync("/api/rules", postBody);
        postResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var posted = await postResp.Content.ReadFromJsonAsync<RuleDto>();
        posted!.Id.Should().BeGreaterThan(0);

        // GET
        var getResp = await client.GetAsync("/api/rules");
        getResp.IsSuccessStatusCode.Should().BeTrue();
        var list = await getResp.Content.ReadFromJsonAsync<List<RuleDto>>();
        list!.Should().ContainSingle(r => r.Id == posted.Id);

        // DELETE
        var delResp = await client.DeleteAsync($"/api/rules/{posted.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // GET nach DELETE
        var getEmptyResp = await client.GetAsync("/api/rules");
        var empty = await getEmptyResp.Content.ReadFromJsonAsync<List<RuleDto>>();
        empty!.Should().NotContain(r => r.Id == posted.Id);
    }

    [Fact]
    public async Task Post_WithEmptyText_Returns400()
    {
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/rules", new
        {
            text = "",
            daysOfWeek = (int)DayOfWeekFlags.AllDays,
            timeRangeStart = (string?)null,
            timeRangeEnd = (string?)null,
            hardness = "soft",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_NonexistentId_Returns404()
    {
        var client = _factory.CreateClient();

        var resp = await client.DeleteAsync("/api/rules/99999");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record RuleDto(
        long Id,
        string Text,
        int DaysOfWeek,
        string? TimeRangeStart,
        string? TimeRangeEnd,
        string Hardness,
        DateTimeOffset CreatedAt);
}
