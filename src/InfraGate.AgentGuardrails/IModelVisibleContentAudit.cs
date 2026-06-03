namespace InfraGate.AgentGuardrails;

public interface IModelVisibleContentAudit
{
    Task PersistAsync(
        string digest,
        ModelVisibleContentSource source,
        string agentName,
        ModelVisibleContentDecision decision,
        CancellationToken cancellationToken);
}
