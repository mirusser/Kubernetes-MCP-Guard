namespace InfraGate.Approvals;

public sealed record AuthorizationResult(bool IsAuthorized, string? Reason)
{
    public static AuthorizationResult Authorized() => new(true, null);

    public static AuthorizationResult Denied(string reason) => new(false, reason);
}
