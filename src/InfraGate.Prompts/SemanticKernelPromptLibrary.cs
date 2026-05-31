using Microsoft.SemanticKernel;

namespace InfraGate.Prompts;

internal sealed class SemanticKernelPromptLibrary(IReadOnlyDictionary<string, RegisteredPrompt> templates)
    : IPromptLibrary
{
    private static readonly Kernel emptyKernel = Kernel.CreateBuilder().Build();

    public async Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!templates.TryGetValue(templateName, out var prompt))
            throw new KeyNotFoundException($"Unknown prompt template '{templateName}'.");

        prompt.ValidateRequired(arguments);

        var kernelArgs = new KernelArguments();
        foreach (var (k, v) in arguments)
            kernelArgs[k] = v;

        return await prompt.Template.RenderAsync(emptyKernel, kernelArgs, cancellationToken).ConfigureAwait(false);
    }
}
