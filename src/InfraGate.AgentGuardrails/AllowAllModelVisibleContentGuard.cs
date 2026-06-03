namespace InfraGate.AgentGuardrails;

public sealed class AllowAllModelVisibleContentGuard : IModelVisibleContentGuard
{
    public static readonly AllowAllModelVisibleContentGuard Instance = new();

    public Task<ModelVisibleContentDecision> EvaluateAsync(
        ModelVisibleContent content,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ModelVisibleContentDecision(
            ModelVisibleContentAction.Allow,
            content.Text,
            [],
            AgentGuardrailConventions.Reasons.None));
    }
}
