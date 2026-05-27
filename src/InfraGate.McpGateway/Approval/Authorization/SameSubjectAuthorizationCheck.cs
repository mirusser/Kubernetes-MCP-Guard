using InfraGate.Approvals;

namespace InfraGate.McpGateway;

public sealed class SameSubjectAuthorizationCheck : IAuthorizationCheck
{
    public Task<AuthorizationResult> EvaluateAsync(IAuthorizationContext context, CancellationToken cancellationToken)
    {
        var result = string.Equals(context.RequesterSubject, context.ActorSubject, StringComparison.Ordinal)
            ? AuthorizationResult.Authorized()
            : AuthorizationResult.Denied("Actor subject does not match the plan requester subject.");

        return Task.FromResult(result);
    }
}
