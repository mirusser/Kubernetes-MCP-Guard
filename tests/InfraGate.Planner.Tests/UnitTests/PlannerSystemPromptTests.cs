using System.Text;
using InfraGate.Planner.Cycle;
using InfraGate.Prompts;
using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.Planner.Tests.UnitTests;

public sealed class PlannerSystemPromptTests
{
    private static async Task<IPromptLibrary> BuildPlannerLibraryAsync()
    {
        var assembly = typeof(BatchProcessor).Assembly;
        using var stream = assembly.GetManifestResourceStream(PlannerConventions.Prompts.SystemPromptResourceName)
            ?? throw new InvalidOperationException("Embedded resource not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string templateText = await reader.ReadToEndAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(b => b.AddTemplate(
            PlannerConventions.Prompts.SystemPromptTemplateName,
            templateText,
            []));

        return services.BuildServiceProvider().GetRequiredService<IPromptLibrary>();
    }

    [Fact]
    public async Task RenderAsync_TreatsToolResultPayloadsAsUntrustedObservations()
    {
        var library = await BuildPlannerLibraryAsync();
        string prompt = await library.RenderAsync(
            PlannerConventions.Prompts.SystemPromptTemplateName,
            new Dictionary<string, object?>(StringComparer.Ordinal));

        Assert.Contains("untrusted", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observation data", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not instructions", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
