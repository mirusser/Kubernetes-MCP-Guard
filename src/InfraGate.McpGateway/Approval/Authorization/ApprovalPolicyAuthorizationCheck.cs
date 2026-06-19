using InfraGate.Approvals;
using InfraGate.Approvals.Plan;
using InfraGate.Approvals.PreExecution;

namespace InfraGate.McpGateway;

public sealed class ApprovalPolicyAuthorizationCheck : IAuthorizationCheck
{
    public Task<AuthorizationResult> EvaluateAsync(IAuthorizationContext context, CancellationToken cancellationToken)
    {
        AuthorizationResult result = context.ApprovalPolicy.Type switch
        {
            ApprovalConventions.ApprovalPolicyTypes.SameSubject =>
                SameSubject(context),
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval =>
                AuthorizationResult.Authorized(),
            _ => AuthorizationResult.Denied(
                $"Unsupported approval policy '{context.ApprovalPolicy.Type}'.")
        };

        return Task.FromResult(result);
    }

    private static AuthorizationResult SameSubject(IAuthorizationContext context) =>
        string.Equals(context.RequesterSubject, context.ActorSubject, StringComparison.Ordinal)
            ? AuthorizationResult.Authorized()
            : AuthorizationResult.Denied("Actor subject does not match the plan requester subject.");
}
