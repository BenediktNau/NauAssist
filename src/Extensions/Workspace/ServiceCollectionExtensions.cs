using Microsoft.Extensions.DependencyInjection;

namespace NauAssist.Extensions.Workspace;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExtensionWorkspace(this IServiceCollection services)
    {
        services.AddSingleton<IExtensionAuditLog, FileExtensionAuditLog>();
        services.AddSingleton<IExtensionWorkspace, ExtensionWorkspace>();
        return services;
    }
}
