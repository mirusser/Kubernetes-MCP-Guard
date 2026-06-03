namespace InfraGate.AgentGuardrails;

public interface IModelVisibleContentGuard
{
    Task<ModelVisibleContentDecision> EvaluateAsync(
        ModelVisibleContent content,
        CancellationToken cancellationToken);
}
