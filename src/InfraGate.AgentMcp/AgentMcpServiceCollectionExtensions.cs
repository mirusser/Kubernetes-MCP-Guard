using InfraGate.ClientCredentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InfraGate.AgentMcp;

public static class AgentMcpServiceCollectionExtensions
{
    public static IServiceCollection AddInfraGateAgentMcp(
        this IServiceCollection services,
        AgentMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        services.TryAddSingleton<IAgentMcpToolset>(sp => new AgentMcpToolset(
            options,
            sp.GetRequiredService<IClientCredentialsTokenProvider>(),
            sp.GetRequiredService<ILoggerFactory>()));

        return services;
    }
}
