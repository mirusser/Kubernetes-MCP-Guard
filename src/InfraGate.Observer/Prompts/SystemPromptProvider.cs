using System.Reflection;

namespace InfraGate.Observer.Prompts;

internal sealed class SystemPromptProvider : ISystemPromptProvider
{
    private static readonly string ResourceName = typeof(SystemPromptProvider).Namespace + ".ObserverSystemPrompt.md";

    private readonly Lazy<string> promptTemplate;

    public SystemPromptProvider()
    {
        promptTemplate = new Lazy<string>(LoadPromptTemplate);
    }

    public string Get(string namespaceName, int maxToolIterations)
    {
        var prompt = promptTemplate.Value;
        prompt = prompt.Replace("{NAMESPACE}", namespaceName, StringComparison.Ordinal);
        prompt = prompt.Replace("{MAX_TOOL_ITERATIONS}", maxToolIterations.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return prompt;
    }

    private static string LoadPromptTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Ensure ObserverSystemPrompt.md is an EmbeddedResource in the csproj.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
