using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NauAssist.Common.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registriert sämtliche typisierten Options-Bindings und den
    /// <see cref="IPathResolver"/>. Validierung läuft beim Start; fehlt ein
    /// Pflichtfeld, schlägt der Host hart fehl.
    /// </summary>
    public static IServiceCollection AddNauAssistConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PathOptions>()
            .Bind(configuration.GetSection(PathOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MemoryOptions>()
            .Bind(configuration.GetSection(MemoryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SchedulerOptions>()
            .Bind(configuration.GetSection(SchedulerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SourcesOptions>()
            .Bind(configuration.GetSection(SourcesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<VoiceOptions>()
            .Bind(configuration.GetSection(VoiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPathResolver, PathResolver>();

        return services;
    }
}
