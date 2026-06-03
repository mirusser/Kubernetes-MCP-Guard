namespace InfraGate.AgentGuardrails;

public sealed record class ModelVisibleContentOptions
{
    public const string SectionName = "InfraGate:AgentGuardrails:ModelVisibleContent";

    public bool Enabled { get; init; } = true;
    public bool SemanticClassifierEnabled { get; init; }
    public string LocalClassifierBaseUrl { get; init; } = string.Empty;
    public int RequestTimeoutMilliseconds { get; init; } = 1_000;
    public int MaximumInputCharacters { get; init; } = 100_000;
    public ModelVisibleContentUnavailableBehavior UnavailableBehavior { get; init; } =
        ModelVisibleContentUnavailableBehavior.FailClosed;
    public string QuarantinePlaceholder { get; init; } =
        AgentGuardrailConventions.DefaultQuarantinePlaceholder;

    public void Validate()
    {
        if (RequestTimeoutMilliseconds <= 0)
            throw new InvalidOperationException("RequestTimeoutMilliseconds must be greater than zero.");

        if (MaximumInputCharacters <= 0)
            throw new InvalidOperationException("MaximumInputCharacters must be greater than zero.");

        if (!Enum.IsDefined(UnavailableBehavior))
            throw new InvalidOperationException("UnavailableBehavior must be a defined model-visible content behavior.");

        if (string.IsNullOrWhiteSpace(QuarantinePlaceholder))
            throw new InvalidOperationException("QuarantinePlaceholder must be configured.");

        if (!string.IsNullOrWhiteSpace(LocalClassifierBaseUrl) &&
            !Uri.TryCreate(LocalClassifierBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("LocalClassifierBaseUrl must be an absolute URI when configured.");
        }

        if (SemanticClassifierEnabled)
        {
            throw new InvalidOperationException(
                "SemanticClassifierEnabled is not supported until the local classifier adapter is implemented.");
        }
    }
}
