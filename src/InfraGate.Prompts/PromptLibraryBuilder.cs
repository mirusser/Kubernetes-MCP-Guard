using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;

namespace InfraGate.Prompts;

public sealed class PromptLibraryBuilder
{
    private static readonly HandlebarsPromptTemplateFactory handlebarsFactory = new();
    private readonly Dictionary<string, RegisteredPrompt> templates = new(StringComparer.Ordinal);

    public PromptLibraryBuilder AddTemplate(
        string name,
        string templateText,
        IReadOnlyList<string>? requiredVariables = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(templateText);

        var config = new PromptTemplateConfig(templateText)
        {
            TemplateFormat = HandlebarsPromptTemplateFactory.HandlebarsTemplateFormat,
        };

        var template = handlebarsFactory.Create(config);
        templates[name] = new RegisteredPrompt(name, template, requiredVariables ?? []);
        return this;
    }

    internal IReadOnlyDictionary<string, RegisteredPrompt> Build() =>
        templates.AsReadOnly();
}
