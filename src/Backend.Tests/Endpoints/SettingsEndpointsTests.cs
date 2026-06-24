using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class SettingsEndpointsTests : IDisposable
{
    private readonly TestAppFactory _factory = new();

    [Fact]
    public async Task Get_ReturnsDefaultOllamaModel()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/settings/llm");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LlmSettingsDto>();

        body!.OllamaModel.Should().Be("gemma4:26b");
        body.SystemPrompt.Should().BeNull();
        body.DefaultSystemPrompt.Should().NotBeNull();
    }

    [Fact]
    public async Task Put_Valid_ReturnsOk_AndGetReflectsChange()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            ollamaModel = "mistral:7b",
            systemPrompt = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        using var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.OllamaModel.Should().Be("mistral:7b");
        body.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Put_SystemPrompt_PersistsAndIsReturned()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            ollamaModel = "mistral:7b",
            systemPrompt = "Du bist ein hilfreicher Agent.",
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        using var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.SystemPrompt.Should().Be("Du bist ein hilfreicher Agent.");
    }

    [Fact]
    public async Task Put_EmptySystemPrompt_ResetsToFallback()
    {
        using var client = _factory.CreateClient();
        await client.PutAsJsonAsync("/api/settings/llm", new
        {
            ollamaModel = "mistral:7b",
            systemPrompt = "something",
        });

        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            ollamaModel = "mistral:7b",
            systemPrompt = "",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        using var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.SystemPrompt.Should().BeNull();
    }

    [Fact]
    public async Task Put_EmptyOllamaModel_Returns400()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            ollamaModel = "",
            systemPrompt = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose() => _factory.Dispose();

    private sealed record LlmSettingsDto(
        string OllamaModel,
        string? SystemPrompt,
        string DefaultSystemPrompt);
}
