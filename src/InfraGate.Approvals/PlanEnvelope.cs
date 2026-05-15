using System.Text.Json;

namespace InfraGate.Approvals;

public sealed record PlanEnvelope
{
    public PlanEnvelope() { }

    public PlanEnvelope(
        string id,
        string adapterId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanRequester requester,
        JsonElement payload)
    {
        Id = id;
        AdapterId = adapterId;
        Operation = operation;
        CreatedAtUtc = createdAtUtc;
        Requester = requester;
        Payload = payload;
    }

    public string Id { get; init; } = string.Empty;

    public string AdapterId { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public PlanRequester Requester { get; init; } = new(string.Empty, null);

    public JsonElement Payload { get; init; }
}
