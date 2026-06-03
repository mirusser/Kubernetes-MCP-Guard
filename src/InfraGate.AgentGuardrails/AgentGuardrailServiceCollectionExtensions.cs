using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InfraGate.AgentGuardrails;

public static class AgentGuardrailServiceCollectionExtensions
{
    public static IServiceCollection AddAgentGuardrails(this IServiceCollection services)
    {
        services.TryAddSingleton<AgentGuardrailMetrics>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="AllowAllModelVisibleContentGuard"/> as the <see cref="IModelVisibleContentGuard"/>
    /// implementation. Use in development and test environments only — it passes all content through unchanged.
    /// </summary>
    public static IServiceCollection AddAllowAllModelVisibleContentGuard(this IServiceCollection services)
    {
        services.TryAddSingleton<IModelVisibleContentGuard>(AllowAllModelVisibleContentGuard.Instance);
        return services;
    }
}
