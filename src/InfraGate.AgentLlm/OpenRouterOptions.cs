namespace InfraGate.AgentLlm;

public sealed record class OpenRouterOptions
{
    public const string SectionName = "InfraGate:OpenRouter";
    public const string ApiKeyConfigurationKey = "InfraGate:OpenRouter:ApiKey";
    public const string ApiKeyEnvironmentVariable = "InfraGate__OpenRouter__ApiKey";

    public string ApiKey { get; init; } = string.Empty;
}
