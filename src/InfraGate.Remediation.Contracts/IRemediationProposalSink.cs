namespace InfraGate.Remediation.Contracts;

public interface IRemediationProposalSink
{
    Task PublishAsync(RemediationProposalBatch batch, CancellationToken cancellationToken);
}
