using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.Prompts;

public static class PromptLibraryServiceCollectionExtensions
{
    public static IServiceCollection AddInfraGatePromptLibrary(
        this IServiceCollection services,
        Action<PromptLibraryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PromptLibraryBuilder();
        configure(builder);
        var templates = builder.Build();

        services.AddSingleton<IPromptLibrary>(new SemanticKernelPromptLibrary(templates));
        return services;
    }
}
