namespace InfraGate.Approvals;

public interface IAuthorizationCheck
{
    Task<AuthorizationResult> EvaluateAsync(IAuthorizationContext context, CancellationToken cancellationToken);
}
