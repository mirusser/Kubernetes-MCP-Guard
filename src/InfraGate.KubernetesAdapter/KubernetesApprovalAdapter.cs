using System.Text.Json;
using InfraGate.Approvals;

namespace InfraGate.KubernetesAdapter;

public static class KubernetesApprovalAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static PlanEnvelope<KubernetesPlanPayload> CreateEnvelope(
        string planId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanRequester requester,
        KubernetesPlanPayload payload)
    {
        var intentDigest = ComputeIntentDigest(operation, payload);

        return PlanEnvelopeFactory.Create(
            planId,
            KubernetesAdapterConventions.AdapterId,
            operation,
            createdAtUtc,
            requester,
            intentDigest,
            payload);
    }

    public static PlanEnvelope<KubernetesPlanPayload> WithPayload(
        PlanEnvelope<KubernetesPlanPayload> envelope,
        KubernetesPlanPayload payload)
    {
        return CreateEnvelope(
            envelope.Id,
            envelope.Operation,
            envelope.CreatedAtUtc,
            envelope.Requester,
            payload);
    }

    public static KubernetesPlan Materialize(PlanEnvelope<KubernetesPlanPayload> envelope) =>
        new(ToEnvelope(envelope), envelope.Payload);

    public static PlanEnvelope ToEnvelope(PlanEnvelope<KubernetesPlanPayload> envelope)
    {
        var payload = JsonSerializer.SerializeToElement(envelope.Payload, JsonOptions);
        return new PlanEnvelope(
            envelope.Id,
            envelope.Profile,
            envelope.AdapterId,
            envelope.Operation,
            envelope.CreatedAtUtc,
            envelope.ValidFromUtc,
            envelope.ValidUntilUtc,
            envelope.Requester,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            payload);
    }

    public static KubernetesPlanDecodeResult Decode(PlanEnvelope envelope)
    {
        if (!string.Equals(envelope.AdapterId, KubernetesAdapterConventions.AdapterId, StringComparison.Ordinal))
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' uses unsupported adapter '{envelope.AdapterId}'.");
        }

        KubernetesPlanPayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions);
        }
        catch (JsonException ex)
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' Kubernetes payload could not be read: {ex.Message}");
        }

        return payload is null
            ? KubernetesPlanDecodeResult.Failed($"Plan '{envelope.Id}' Kubernetes payload could not be read.")
            : KubernetesPlanDecodeResult.Success(new KubernetesPlan(envelope, payload));
    }

    private static ApprovalDigest ComputeIntentDigest(string operation, KubernetesPlanPayload payload)
    {
        return ApprovalDigest.ComputeSha256(
            KubernetesAdapterConventions.Canonicalizations.IntentV1,
            new
            {
                operation,
                @namespace = payload.Namespace,
                payload.Parameters,
                payload.Objects,
                payload.Manifest
            });
    }
}
