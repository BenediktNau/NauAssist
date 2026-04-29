using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NauAssist.Common.Configuration;

namespace NauAssist.Tests.Configuration;

public class OptionsValidationTests
{
    [Fact]
    public void LlmOptions_RejectsEmptyEndpoint_E0_2()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Llm:Model"] = "gemma4:e4b",
            ["Llm:Endpoint"] = "",
        });

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LlmOptions>>().Value);

        Assert.Contains(nameof(LlmOptions.Endpoint), ex.Message);
    }

    [Fact]
    public void LlmOptions_RejectsTemperatureOutOfRange_E0_2()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Llm:Temperature"] = "5.0",
        });

        var ex = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LlmOptions>>().Value);

        Assert.Contains(nameof(LlmOptions.Temperature), ex.Message);
    }

    [Fact]
    public void SchedulerOptions_RejectsInvalidQuietHours_E0_2()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Scheduler:QuietHoursStart"] = "Mitternacht",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SchedulerOptions>>().Value);
    }

    [Fact]
    public void EnvOverride_BeatsConfiguration_E0_2()
    {
        const string envKey = "Llm__Model";
        Environment.SetEnvironmentVariable(envKey, "override-modell");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:Model"] = "appsettings-modell",
                }!)
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection().AddNauAssistConfiguration(configuration);
            using var provider = services.BuildServiceProvider();

            var resolved = provider.GetRequiredService<IOptions<LlmOptions>>().Value;

            Assert.Equal("override-modell", resolved.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public void DefaultConfiguration_IsValid_E0_2()
    {
        var provider = BuildProvider(new Dictionary<string, string?>());

        var llm = provider.GetRequiredService<IOptions<LlmOptions>>().Value;
        var memory = provider.GetRequiredService<IOptions<MemoryOptions>>().Value;
        var scheduler = provider.GetRequiredService<IOptions<SchedulerOptions>>().Value;
        var sources = provider.GetRequiredService<IOptions<SourcesOptions>>().Value;
        var voice = provider.GetRequiredService<IOptions<VoiceOptions>>().Value;
        var paths = provider.GetRequiredService<IOptions<PathOptions>>().Value;

        Assert.Equal("gemma4:e4b", llm.Model);
        Assert.Equal("memory.db", memory.DatabaseFile);
        Assert.Equal(TimeSpan.FromMinutes(5), scheduler.ReflectionInterval);
        Assert.NotNull(sources.Mailboxes);
        Assert.Equal("de_DE-thorsten-medium", voice.PiperVoice);
        Assert.Equal("extensions", paths.ExtensionsRoot);
    }

    private static ServiceProvider BuildProvider(IDictionary<string, string?> overrides)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides!)
            .Build();

        return new ServiceCollection()
            .AddNauAssistConfiguration(configuration)
            .BuildServiceProvider();
    }
}
