using System.Text.Json.Serialization;

namespace InfraGate.Approvals;

public sealed record class ChallengeOutcome
{
    [JsonConstructor]
    public ChallengeOutcome(
        string id,
        string challengeId,
        string status,
        string? actorSubject,
        DateTimeOffset decidedAtUtc,
        string? reason,
        string? grantId)
    {
        Id = id;
        ChallengeId = challengeId;
        Status = status;
        ActorSubject = actorSubject;
        DecidedAtUtc = decidedAtUtc;
        Reason = reason;
        GrantId = grantId;
    }

    public ChallengeOutcome(
        string status,
        string? actorSubject,
        DateTimeOffset decidedAtUtc,
        string? reason,
        string? grantId)
        : this(
            ApprovalIds.NewChallengeOutcomeId(),
            string.Empty,
            status,
            actorSubject,
            decidedAtUtc,
            reason,
            grantId)
    {
    }

    public string Id { get; init; }

    public string ChallengeId { get; init; }

    public string Status { get; init; }

    public string? ActorSubject { get; init; }

    public DateTimeOffset DecidedAtUtc { get; init; }

    public string? Reason { get; init; }

    public string? GrantId { get; init; }
}
