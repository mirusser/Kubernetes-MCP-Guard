namespace InfraGate.Approvals;

public sealed record PlanEnvelope<TPayload>(
    string Id,
    string AdapterId,
    string Operation,
    DateTimeOffset CreatedAtUtc,
    PlanRequester Requester,
    TPayload Payload);
