namespace InfraGate.AgentGuardrails;

public static class AgentGuardrailServiceCollectionExtensions
{
    public static IServiceCollection AddAgentGuardrails(this IServiceCollection services)
    {
        services.AddSingleton<AgentGuardrailMetrics>();
        return services;
    }
}
