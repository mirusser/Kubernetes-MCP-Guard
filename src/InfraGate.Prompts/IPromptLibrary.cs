namespace InfraGate.Prompts;

public interface IPromptLibrary
{
    Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}
