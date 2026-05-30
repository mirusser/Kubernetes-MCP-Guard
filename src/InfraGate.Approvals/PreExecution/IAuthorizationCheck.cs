namespace InfraGate.Approvals.PreExecution;

public interface IAuthorizationCheck
{
    Task<AuthorizationResult> EvaluateAsync(IAuthorizationContext context, CancellationToken cancellationToken);
}
