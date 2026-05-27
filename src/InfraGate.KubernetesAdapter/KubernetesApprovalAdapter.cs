using System.Text.Json;
using InfraGate.Approvals;
using InfraGate.Approvals.Plan;

namespace InfraGate.KubernetesAdapter;

internal static class KubernetesApprovalAdapter
{
    private static readonly ReviewSurfaceContext DefaultReviewSurfaceContext = new(
        ApprovalConventions.ReviewSurfaces.GatewayBrowser,
        KubernetesAdapterConventions.ReviewRenderers.PlanReviewV1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static PlanEnvelope<KubernetesPlanPayload> CreateEnvelope( // NOSONAR:S107 - Generic envelope creation has eight approval-domain fields; grouping them would hide the digest-binding contract.
        string planId,
        string operation,
        DateTimeOffset createdAtUtc,
        PlanRequester requester,
        KubernetesPlanPayload payload,
        ReviewSurfaceContext? reviewSurfaceContext = null,
        FreshnessPolicy? freshnessPolicy = null,
        ApprovalPolicy? approvalPolicy = null)
    {
        var intentDigest = ComputeIntentDigest(operation, payload);

        return PlanEnvelopeFactory.Create(
            planId,
            KubernetesAdapterConventions.AdapterId,
            operation,
            createdAtUtc,
            requester,
            intentDigest,
            reviewSurfaceContext ?? DefaultReviewSurfaceContext,
            payload,
            freshnessPolicy,
            BuildEvidenceArtifacts(payload),
            approvalPolicy);
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
            payload,
            envelope.ReviewSurfaceContext,
            envelope.FreshnessPolicy,
            envelope.ApprovalPolicy);
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
            envelope.FreshnessPolicy,
            envelope.ReviewSurfaceContext,
            envelope.EvidenceArtifacts,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            payload);
    }

    public static KubernetesPlanDecodeResult Decode(PlanEnvelope envelope)
    {
        if (!string.Equals(envelope.AdapterId, KubernetesAdapterConventions.AdapterId, StringComparison.Ordinal))
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' uses unsupported adapter '{envelope.AdapterId}'.",
                KubernetesAdapterConventions.ResultReasonCodes.UnsupportedAdapter);
        }

        KubernetesPlanPayload? payload;
        try
        {
            payload = envelope.Payload.Deserialize<KubernetesPlanPayload>(JsonOptions);
        }
        catch (JsonException ex)
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' Kubernetes payload could not be read: {ex.Message}",
                KubernetesAdapterConventions.ResultReasonCodes.PayloadReadFailed);
        }

        if (payload is null)
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' Kubernetes payload could not be read.",
                KubernetesAdapterConventions.ResultReasonCodes.PayloadReadFailed);
        }

        var expectedIntentDigest = ComputeIntentDigest(envelope.Operation, payload);
        if (envelope.IntentDigest != expectedIntentDigest)
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' Kubernetes intent digest no longer matches the payload.",
                KubernetesAdapterConventions.ResultReasonCodes.IntentDigestChanged);
        }

        var expectedArtifacts = BuildEvidenceArtifacts(payload);
        if (!SameEvidenceArtifacts(envelope.EvidenceArtifacts, expectedArtifacts))
        {
            return KubernetesPlanDecodeResult.Failed(
                $"Plan '{envelope.Id}' Kubernetes evidence artifact summaries no longer match the payload.",
                KubernetesAdapterConventions.ResultReasonCodes.EvidenceDigestChanged);
        }

        return KubernetesPlanDecodeResult.Success(new KubernetesPlan(envelope, payload));
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

    private static EvidenceArtifactSummary[] BuildEvidenceArtifacts(KubernetesPlanPayload payload)
    {
        var artifacts = new List<EvidenceArtifactSummary>();

        if (payload.DryRun is not null)
        {
            artifacts.Add(new EvidenceArtifactSummary(
                KubernetesAdapterConventions.EvidenceArtifactTypes.DryRun,
                ApprovalDigest.ComputeSha256(
                    KubernetesAdapterConventions.Canonicalizations.DryRunEvidenceV1,
                    payload.DryRun),
                "payload.dryRun",
                []));
        }

        if (payload.Diffs.Length > 0)
        {
            artifacts.Add(new EvidenceArtifactSummary(
                KubernetesAdapterConventions.EvidenceArtifactTypes.Diff,
                ApprovalDigest.ComputeSha256(
                    KubernetesAdapterConventions.Canonicalizations.DiffEvidenceV1,
                    payload.Diffs),
                "payload.diffs",
                []));
        }

        artifacts.Add(new EvidenceArtifactSummary(
            KubernetesAdapterConventions.EvidenceArtifactTypes.PolicyFindings,
            ApprovalDigest.ComputeSha256(
                KubernetesAdapterConventions.Canonicalizations.PolicyFindingsEvidenceV1,
                payload.PolicyFindings),
            "payload.policyFindings",
            []));

        return artifacts.ToArray();
    }

    private static bool SameEvidenceArtifacts(
        IReadOnlyList<EvidenceArtifactSummary> left,
        IReadOnlyList<EvidenceArtifactSummary> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!SameEvidenceArtifact(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameEvidenceArtifact(EvidenceArtifactSummary left, EvidenceArtifactSummary right)
    {
        return string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
               string.Equals(left.Reference, right.Reference, StringComparison.Ordinal) &&
               left.Digest == right.Digest &&
               SameMetadata(left.RedactionMetadata, right.RedactionMetadata);
    }

    private static bool SameMetadata(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        left ??= new Dictionary<string, string>(StringComparer.Ordinal);
        right ??= new Dictionary<string, string>(StringComparer.Ordinal);

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
