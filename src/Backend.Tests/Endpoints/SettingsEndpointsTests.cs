using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Endpoints;

public sealed class SettingsEndpointsTests : IDisposable
{
    private readonly TestAppFactory _factory = new();

    [Fact]
    public async Task Get_ReturnsDefaults_NoApiKeyExposed()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/settings/llm");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LlmSettingsDto>();

        body!.Provider.Should().Be("ollama");
        body.OllamaModel.Should().Be("gemma4:26b");
        body.GeminiModel.Should().Be("gemini-2.5-flash");
        body.HasGeminiApiKey.Should().BeFalse();
    }

    [Fact]
    public async Task Put_Valid_ReturnsOk_AndGetReflectsChange()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "gemini",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "AIza-test",
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK);

        using var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.Provider.Should().Be("gemini");
        body.HasGeminiApiKey.Should().BeTrue();
    }

    [Fact]
    public async Task Put_InvalidProvider_Returns400()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "anthropic",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_SwitchToGeminiWithoutKey_Returns400()
    {
        using var client = _factory.CreateClient();
        using var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "gemini",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = (string?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_EmptyKey_DeletesExistingKey()
    {
        using var client = _factory.CreateClient();
        await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "ollama",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "AIza-keepme",
        });

        var put = await client.PutAsJsonAsync("/api/settings/llm", new
        {
            provider = "ollama",
            ollamaModel = "gemma4:26b",
            geminiModel = "gemini-2.5-flash",
            geminiApiKey = "",
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync("/api/settings/llm");
        var body = await get.Content.ReadFromJsonAsync<LlmSettingsDto>();
        body!.HasGeminiApiKey.Should().BeFalse();
    }

    public void Dispose() => _factory.Dispose();

    private sealed record LlmSettingsDto(
        string Provider,
        string OllamaModel,
        string GeminiModel,
        bool HasGeminiApiKey);
}
