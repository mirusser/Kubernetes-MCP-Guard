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
        List<string>? missing = null;
        foreach (var v in RequiredVariables)
        {
            if (!arguments.ContainsKey(v))
                (missing ??= []).Add(v);
        }

        if (missing is not null)
            throw new ArgumentException(
                $"Missing required template variables for '{Name}': {string.Join(", ", missing)}",
                nameof(arguments));
    }
}
