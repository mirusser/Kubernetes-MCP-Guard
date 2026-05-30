using Microsoft.SemanticKernel;

namespace InfraGate.Prompts;

internal sealed class RegisteredPrompt
{
    internal RegisteredPrompt(string name, IPromptTemplate template, IReadOnlyList<string> requiredVariables)
    {
        Name = name;
        Template = template;
        RequiredVariables = requiredVariables;
    }

    internal string Name { get; }
    internal IPromptTemplate Template { get; }
    internal IReadOnlyList<string> RequiredVariables { get; }

    internal void ValidateRequired(IReadOnlyDictionary<string, object?> arguments)
    {
        var missing = RequiredVariables
            .Where(v => !arguments.ContainsKey(v))
            .ToList();

        if (missing.Count > 0)
            throw new ArgumentException(
                $"Missing required template variables for '{Name}': {string.Join(", ", missing)}",
                nameof(arguments));
    }
}
