using InfraGate.AgentGuardrails.AgentGovernanceToolkit;

namespace InfraGate.AgentGuardrails;

public static class AgentGovernanceToolkitContentGuardServiceCollectionExtensions
{
    public static IServiceCollection AddAgentGovernanceToolkitContentGuard(
        this IServiceCollection services,
        DetectionConfig? config = null)
    {
        services.AddSingleton(new AgentGovernanceToolkitContentGuard(
            new PromptInjectionDetector(config ?? new DetectionConfig())));
        return services;
    }

    /// <summary>
    /// Registers the default model-visible content guard. When <paramref name="options"/> has
    /// <see cref="ModelVisibleContentOptions.Enabled"/> set to <c>false</c>, registers the
    /// <see cref="AllowAllModelVisibleContentGuard"/>. Otherwise composes the AGT deterministic
    /// adapter inside a <see cref="CompositeModelVisibleContentGuard"/>.
    /// </summary>
    public static IServiceCollection AddModelVisibleContentGuard(
        this IServiceCollection services,
        ModelVisibleContentOptions options,
        DetectionConfig? agtConfig = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        services.AddAgentGuardrails();

        if (!options.Enabled)
        {
            return services.AddAllowAllModelVisibleContentGuard();
        }

        var agtGuard = new AgentGovernanceToolkitContentGuard(
            new PromptInjectionDetector(agtConfig ?? new DetectionConfig()));
        services.AddSingleton(agtGuard);
        services.AddSingleton(options);
        services.AddSingleton<IModelVisibleContentGuard>(sp =>
        {
            return new CompositeModelVisibleContentGuard(
                [sp.GetRequiredService<AgentGovernanceToolkitContentGuard>()],
                sp.GetRequiredService<AgentGuardrailMetrics>(),
                sp.GetRequiredService<ILogger<CompositeModelVisibleContentGuard>>(),
                sp.GetRequiredService<ModelVisibleContentOptions>(),
                sp.GetService<IModelVisibleContentAudit>());
        });

        return services;
    }
}
