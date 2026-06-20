using System.Reflection;
using System.Text;
using InfraGate.Observer.Cycle;
using InfraGate.Prompts;
using Microsoft.Extensions.DependencyInjection;

namespace InfraGate.Observer.Tests.UnitTests;

public sealed class SystemPromptProviderTests
{
    private static async Task<IPromptLibrary> BuildObserverLibraryAsync()
    {
        var assembly = typeof(ObservationCycleRunner).Assembly;
        using var stream = assembly.GetManifestResourceStream(ObserverConventions.Prompts.SystemPromptResourceName)
            ?? throw new InvalidOperationException("Embedded resource not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var templateText = await reader.ReadToEndAsync().ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddInfraGatePromptLibrary(b => b.AddTemplate(
            ObserverConventions.Prompts.SystemPromptTemplateName,
            templateText,
            [ObserverConventions.Prompts.NamespaceArgumentName, ObserverConventions.Prompts.MaxToolIterationsArgumentName]));

        return services.BuildServiceProvider().GetRequiredService<IPromptLibrary>();
    }

    private static Dictionary<string, object?> DefaultArgs(string ns = "default", int maxIter = 8) =>
        new(StringComparer.Ordinal) { ["namespace"] = ns, ["maxToolIterations"] = maxIter };

    [Fact]
    public async Task RenderAsync_SubstitutesNamespacePlaceholder()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName,
            new Dictionary<string, object?>(StringComparer.Ordinal) { [ObserverConventions.Prompts.NamespaceArgumentName] = "mcp-nginx-demo", [ObserverConventions.Prompts.MaxToolIterationsArgumentName] = 8 });

        Assert.DoesNotContain("{{namespace}}", prompt, StringComparison.Ordinal);
        Assert.Contains("mcp-nginx-demo", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_SubstitutesMaxToolIterationsPlaceholder()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName,
            new Dictionary<string, object?>(StringComparer.Ordinal) { [ObserverConventions.Prompts.NamespaceArgumentName] = "test-ns", [ObserverConventions.Prompts.MaxToolIterationsArgumentName] = 3 });

        Assert.DoesNotContain("{{maxToolIterations}}", prompt, StringComparison.Ordinal);
        Assert.Contains("3", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_NoUnresolvedPlaceholders()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs());

        Assert.DoesNotContain("{{namespace}}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{{maxToolIterations}}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_TreatsToolResultPayloadsAsUntrustedObservations()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs());

        Assert.Contains("untrusted", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observation data", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not instructions", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_ForbidsMutationToolNames()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs());

        Assert.Contains("request_", prompt, StringComparison.Ordinal);
        Assert.Contains("execute_", prompt, StringComparison.Ordinal);
        Assert.Contains("apply_", prompt, StringComparison.Ordinal);
        Assert.Contains("delete_", prompt, StringComparison.Ordinal);
        Assert.Contains("scale_", prompt, StringComparison.Ordinal);
        Assert.Contains("restart_", prompt, StringComparison.Ordinal);
        Assert.Contains("set_", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_ContainsAllFourAnomalyKinds()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs());

        Assert.Contains("PodUnhealthy", prompt, StringComparison.Ordinal);
        Assert.Contains("DeploymentUnavailable", prompt, StringComparison.Ordinal);
        Assert.Contains("ServiceNoEndpoints", prompt, StringComparison.Ordinal);
        Assert.Contains("WarningEvent", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_ContainsAllReadOnlyTools()
    {
        var library = await BuildObserverLibraryAsync();
        var prompt = await library.RenderAsync(
            ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs());

        string[] readOnlyToolNames =
        [
            ObserverConventions.ToolNames.GetAllowedNamespaces,
            ObserverConventions.ToolNames.GetK8sStatus,
            ObserverConventions.ToolNames.GetK8sEvents,
            ObserverConventions.ToolNames.GetPodLogs,
            ObserverConventions.ToolNames.GetK8sResource,
            ObserverConventions.ToolNames.GetDeploymentDiagnostics,
            ObserverConventions.ToolNames.GetPodDiagnostics,
            ObserverConventions.ToolNames.GetServiceDiagnostics,
        ];
        foreach (var toolName in readOnlyToolNames)
        {
            Assert.Contains(toolName, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RenderAsync_IsDeterministicForSameInput()
    {
        var library = await BuildObserverLibraryAsync();
        var first = await library.RenderAsync(ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs("ns1", 5));
        var second = await library.RenderAsync(ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs("ns1", 5));

        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_DifferentNamespacesProduceDifferentOutput()
    {
        var library = await BuildObserverLibraryAsync();
        var first = await library.RenderAsync(ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs("ns-a", 5));
        var second = await library.RenderAsync(ObserverConventions.Prompts.SystemPromptTemplateName, DefaultArgs("ns-b", 5));

        Assert.NotEqual(first, second, StringComparer.Ordinal);
    }
}
