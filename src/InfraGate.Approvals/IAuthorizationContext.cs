namespace InfraGate.Approvals;

public interface IAuthorizationContext
{
    string RequesterSubject { get; }
    string ActorSubject { get; }
}
