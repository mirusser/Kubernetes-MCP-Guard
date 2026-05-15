using System.Security.Cryptography;
using System.Text.Json;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed class ApprovalStore
{
    private const int PlanIdRandomByteCount = 4;
    private const int GrantIdByteCount = 16;

    private readonly ApprovalStoreOptions options;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ApprovalStore(ApprovalStoreOptions options)
    {
        this.options = options;
    }

    public string PendingDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.PendingDirectory);

    public string ApprovedDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.ApprovedDirectory);

    public string AppliedDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.AppliedDirectory);

    public string ChallengesDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.ChallengesDirectory);

    public string GrantsDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.GrantsDirectory);

    public string AuditPath => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.AuditFileName);

    public static string NewPlanId()
    {
        Span<byte> bytes = stackalloc byte[PlanIdRandomByteCount];
        RandomNumberGenerator.Fill(bytes);

        return $"{DateTimeOffset.UtcNow.ToString(ApprovalConventions.DateTimeFormats.PlanIdTimestamp)}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public async Task<ApprovalPlanResult> CreatePlanAsync<TPayload>(
        PlanEnvelope<TPayload> envelope,
        string targetNamespace,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var pendingPath = GetPendingPath(envelope.Id);
        var json = JsonSerializer.Serialize(envelope, jsonOptions);
        await File.WriteAllTextAsync(pendingPath, json, cancellationToken);

        var hash = await ComputeSha256Async(pendingPath, cancellationToken);
        await WriteAuditAsync(
            ApprovalConventions.AuditEvents.PlanRequested,
            new PlanRequestedPayload(
                envelope.Id,
                envelope.Operation,
                targetNamespace,
                hash,
                envelope.IntentDigest,
                envelope.ReviewDigest),
            cancellationToken);

        return new ApprovalPlanResult(ToUntypedEnvelope(envelope), pendingPath, GetApprovedPath(envelope.Id), hash);
    }

    public async Task<ApprovedPlanResult> GetApprovedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return ApprovedPlanResult.Denied("Plan id contains unsupported characters.");
        }

        var pendingPath = GetPendingPath(planId);
        if (!File.Exists(pendingPath))
        {
            return ApprovedPlanResult.Denied($"No pending plan exists for '{planId}'.");
        }

        var appliedPath = GetAppliedPath(planId);
        if (File.Exists(appliedPath))
        {
            return ApprovedPlanResult.Denied($"Plan '{planId}' was already applied.");
        }

        var approvedPath = GetApprovedPath(planId);
        if (!File.Exists(approvedPath))
        {
            return ApprovedPlanResult.Denied($"Plan '{planId}' is not approved yet.");
        }

        var actualHash = await ComputeSha256Async(pendingPath, cancellationToken);
        var approvedHash = (await File.ReadAllTextAsync(approvedPath, cancellationToken)).Trim();

        if (!FixedTimeStringComparer.Equals(actualHash, approvedHash))
        {
            await WriteAuditAsync(
                ApprovalConventions.AuditEvents.ApprovalHashMismatch,
                new ApprovalHashMismatchPayload(planId, approvedHash, actualHash),
                cancellationToken);

            return ApprovedPlanResult.Denied($"Plan '{planId}' changed after approval; refusing to apply it.");
        }

        var read = await ReadEnvelopeAsync(planId, pendingPath, cancellationToken);

        return read.Envelope is null
            ? ApprovedPlanResult.Denied(read.Message)
            : ApprovedPlanResult.Approved(read.Envelope, actualHash);
    }

    public async Task<GrantedPlanResult> GetGrantedPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return GrantedPlanResult.Denied("Plan id contains unsupported characters.", grantExists: false);
        }

        var pendingPath = GetPendingPath(planId);
        if (!File.Exists(pendingPath))
        {
            return GrantedPlanResult.MissingGrant($"No pending plan exists for '{planId}'.");
        }

        var appliedPath = GetAppliedPath(planId);
        if (File.Exists(appliedPath))
        {
            return GrantedPlanResult.Denied($"Plan '{planId}' was already applied.", grantExists: false);
        }

        var grant = await GetGrantAsync(planId, cancellationToken);
        if (grant is null)
        {
            return GrantedPlanResult.MissingGrant($"Plan '{planId}' is not approved yet.");
        }

        var read = await ReadEnvelopeAsync(planId, pendingPath, cancellationToken);
        if (read.Envelope is null)
        {
            return GrantedPlanResult.Denied(read.Message);
        }

        var validation = ValidateGrant(read.Envelope, grant);

        return validation is null
            ? GrantedPlanResult.Granted(read.Envelope, grant)
            : GrantedPlanResult.Denied(validation);
    }

    public async Task<PendingPlanResult> GetPendingPlanAsync(string planId, CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return PendingPlanResult.Denied("Plan id contains unsupported characters.");
        }

        var pendingPath = GetPendingPath(planId);
        if (!File.Exists(pendingPath))
        {
            return PendingPlanResult.Denied($"No pending plan exists for '{planId}'.");
        }

        var appliedPath = GetAppliedPath(planId);
        if (File.Exists(appliedPath))
        {
            return PendingPlanResult.Denied($"Plan '{planId}' was already applied.");
        }

        var actualHash = await ComputeSha256Async(pendingPath, cancellationToken);
        var read = await ReadEnvelopeAsync(planId, pendingPath, cancellationToken);

        return read.Envelope is null
            ? PendingPlanResult.Denied(read.Message)
            : PendingPlanResult.Found(read.Envelope, pendingPath, GetApprovedPath(planId), actualHash);
    }

    public Task<ApprovedPlanResult> ApprovePendingPlanAsync(
        string planId,
        string expectedHash,
        CancellationToken cancellationToken) =>
        ApprovePendingPlanAsync(
            planId,
            expectedHash,
            ApprovalConventions.ApprovalSources.DirectStore,
            approverSubject: null,
            challengeId: null,
            cancellationToken);

    public async Task<ApprovedPlanResult> ApprovePendingPlanAsync(
        string planId,
        string expectedHash,
        string source,
        string? approverSubject,
        string? challengeId,
        CancellationToken cancellationToken)
    {
        var pending = await GetPendingPlanAsync(planId, cancellationToken);
        if (!pending.IsPending || pending.Envelope is null || pending.Hash is null)
        {
            return ApprovedPlanResult.Denied(pending.Message);
        }

        if (!FixedTimeStringComparer.Equals(expectedHash, pending.Hash))
        {
            await WriteAuditAsync(
                ApprovalConventions.AuditEvents.ApprovalHashMismatch,
                new ApprovalHashMismatchPayload(planId, expectedHash, pending.Hash),
                cancellationToken);

            return ApprovedPlanResult.Denied($"Plan '{planId}' changed during approval; refusing to apply it.");
        }

        EnsureDirectories();
        await File.WriteAllTextAsync(pending.ApprovedPath, pending.Hash, cancellationToken);
        await WriteAuditAsync(
            ApprovalConventions.AuditEvents.PlanApproved,
            new PlanApprovedPayload(planId, pending.Hash, source, approverSubject, challengeId),
            cancellationToken);

        return ApprovedPlanResult.Approved(pending.Envelope, pending.Hash);
    }

    public async Task MarkAppliedAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        string hash,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var appliedPath = GetAppliedPath(envelope.Id);
        var json = JsonSerializer.Serialize(new
        {
            envelope.Id,
            envelope.AdapterId,
            envelope.Operation,
            Namespace = targetNamespace,
            hash,
            appliedAtUtc = DateTimeOffset.UtcNow
        }, jsonOptions);

        await File.WriteAllTextAsync(appliedPath, json, cancellationToken);
        await WriteAuditAsync(
            ApprovalConventions.AuditEvents.PlanApplied,
            new PlanAppliedPayload(envelope.Id, envelope.Operation, targetNamespace, hash),
            cancellationToken);
    }

    public async Task MarkAppliedAsync(
        PlanEnvelope envelope,
        string targetNamespace,
        ApprovalGrant grant,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var appliedPath = GetAppliedPath(envelope.Id);
        var json = JsonSerializer.Serialize(new
        {
            envelope.Id,
            envelope.AdapterId,
            envelope.Operation,
            Namespace = targetNamespace,
            grantId = grant.Id,
            intentDigest = grant.IntentDigest,
            reviewDigest = grant.ReviewDigest,
            appliedAtUtc = DateTimeOffset.UtcNow
        }, jsonOptions);

        await File.WriteAllTextAsync(appliedPath, json, cancellationToken);
        await WriteAuditAsync(
            ApprovalConventions.AuditEvents.PlanApplied,
            new PlanAppliedPayload(envelope.Id, envelope.Operation, targetNamespace, grant.ReviewDigest.Value),
            cancellationToken);
    }

    public async Task<ApprovalGrant> CreateGrantAsync(
        PlanEnvelope envelope,
        string approverSubject,
        string sourceChallengeId,
        CancellationToken cancellationToken)
    {
        EnsureDirectories();
        var issuedAtUtc = DateTimeOffset.UtcNow;
        var grant = new ApprovalGrant(
            NewGrantId(),
            envelope.Id,
            envelope.Requester.Subject,
            approverSubject,
            sourceChallengeId,
            envelope.IntentDigest,
            envelope.ReviewDigest,
            envelope.ApprovalPolicy,
            envelope.ExecutionReusePolicy,
            issuedAtUtc,
            envelope.ValidUntilUtc);

        var json = JsonSerializer.Serialize(grant, jsonOptions);
        await File.WriteAllTextAsync(GetGrantPath(envelope.Id), json, cancellationToken);
        await WriteAuditAsync(
            ApprovalConventions.AuditEvents.GrantIssued,
            new ApprovalGrantIssuedPayload(
                envelope.Id,
                grant.Id,
                sourceChallengeId,
                envelope.Requester.Subject,
                approverSubject,
                envelope.IntentDigest,
                envelope.ReviewDigest,
                grant.ExpiresAtUtc),
            cancellationToken);

        return grant;
    }

    public async Task<ApprovalGrant?> GetGrantAsync(string planId, CancellationToken cancellationToken)
    {
        if (!IsSafePlanId(planId))
        {
            return null;
        }

        var path = GetGrantPath(planId);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);

        return JsonSerializer.Deserialize<ApprovalGrant>(json, jsonOptions);
    }

    public Task WriteAuditAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventName,
            payload
        }, jsonOptions);

        return File.AppendAllTextAsync(AuditPath, line + Environment.NewLine, cancellationToken);
    }

    public string GetPendingPath(string planId) => Path.Combine(PendingDirectory, planId + ApprovalConventions.Storage.JsonExtension);

    public string GetApprovedPath(string planId) => Path.Combine(ApprovedDirectory, planId + ApprovalConventions.Storage.Sha256Extension);

    public string GetGrantPath(string planId) => Path.Combine(GrantsDirectory, planId + ApprovalConventions.Storage.JsonExtension);

    public string GetAppliedPath(string planId) => Path.Combine(AppliedDirectory, planId + ApprovalConventions.Storage.JsonExtension);

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(PendingDirectory);
        Directory.CreateDirectory(ApprovedDirectory);
        Directory.CreateDirectory(AppliedDirectory);
        Directory.CreateDirectory(ChallengesDirectory);
        Directory.CreateDirectory(GrantsDirectory);
    }

    private static string NewGrantId()
    {
        var bytes = new byte[GrantIdByteCount];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsSafePlanId(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return false;
        }

        return planId.All(c => c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-');
    }

    private async Task<EnvelopeReadResult> ReadEnvelopeAsync(
        string planId,
        string pendingPath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(pendingPath, cancellationToken);
        PlanEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<PlanEnvelope>(json, jsonOptions);
        }
        catch (JsonException)
        {
            return EnvelopeReadResult.Failed($"Plan '{planId}' could not be read.");
        }

        if (envelope is null)
        {
            return EnvelopeReadResult.Failed($"Plan '{planId}' could not be read.");
        }

        var validation = ValidateEnvelope(planId, envelope);
        return validation is null
            ? EnvelopeReadResult.Success(envelope)
            : EnvelopeReadResult.Failed(validation);
    }

    private static string? ValidateEnvelope(string planId, PlanEnvelope envelope)
    {
        if (!string.Equals(envelope.Id, planId, StringComparison.Ordinal))
        {
            return $"Plan '{planId}' file contains mismatched plan id '{envelope.Id}'.";
        }

        if (string.IsNullOrWhiteSpace(envelope.AdapterId) ||
            string.IsNullOrWhiteSpace(envelope.Profile) ||
            string.IsNullOrWhiteSpace(envelope.Operation) ||
            string.IsNullOrWhiteSpace(envelope.Requester.Subject) ||
            !IsSupportedPolicy(envelope.ApprovalPolicy) ||
            !IsSupportedReusePolicy(envelope.ExecutionReusePolicy) ||
            IsMissingDigest(envelope.IntentDigest) ||
            IsMissingDigest(envelope.ReviewDigest) ||
            envelope.ValidFromUtc == default ||
            envelope.ValidUntilUtc <= envelope.ValidFromUtc ||
            envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return $"Plan '{planId}' uses an old approval file format. Re-request the plan.";
        }

        return null;
    }

    private static bool IsMissingDigest(ApprovalDigest digest) =>
        !string.Equals(digest.Algorithm, ApprovalConventions.Digests.Sha256, StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(digest.Canonicalization) ||
        string.IsNullOrWhiteSpace(digest.Value);

    private static bool IsSupportedPolicy(ApprovalPolicy policy) =>
        string.Equals(policy.Type, ApprovalConventions.ApprovalPolicyTypes.SameSubject, StringComparison.Ordinal);

    private static bool IsSupportedReusePolicy(ExecutionReusePolicy policy) =>
        string.Equals(policy.Type, ApprovalConventions.ExecutionReusePolicyTypes.SingleExecution, StringComparison.Ordinal);

    private static string? ValidateGrant(PlanEnvelope envelope, ApprovalGrant grant)
    {
        var now = DateTimeOffset.UtcNow;
        if (envelope.ValidFromUtc > now)
        {
            return $"Plan '{envelope.Id}' is not valid yet.";
        }

        if (envelope.ValidUntilUtc <= now)
        {
            return $"Plan '{envelope.Id}' expired before execution.";
        }

        if (grant.ExpiresAtUtc <= now)
        {
            return $"Approval grant '{grant.Id}' expired before execution.";
        }

        if (!string.Equals(grant.PlanId, envelope.Id, StringComparison.Ordinal) ||
            !string.Equals(grant.RequesterSubject, envelope.Requester.Subject, StringComparison.Ordinal) ||
            !SameDigest(grant.IntentDigest, envelope.IntentDigest) ||
            !SameDigest(grant.ReviewDigest, envelope.ReviewDigest) ||
            !SamePolicy(grant.ApprovalPolicy, envelope.ApprovalPolicy) ||
            !SameReusePolicy(grant.ExecutionReusePolicy, envelope.ExecutionReusePolicy))
        {
            return $"Approval grant '{grant.Id}' no longer matches plan '{envelope.Id}'.";
        }

        if (string.Equals(envelope.ApprovalPolicy.Type, ApprovalConventions.ApprovalPolicyTypes.SameSubject, StringComparison.Ordinal) &&
            !string.Equals(grant.RequesterSubject, grant.ApproverSubject, StringComparison.Ordinal))
        {
            return $"Approval grant '{grant.Id}' violates same-subject approval policy.";
        }

        var actualReviewDigest = PlanEnvelopeFactory.ComputeReviewDigest(envelope);
        if (!SameDigest(envelope.ReviewDigest, actualReviewDigest))
        {
            return $"Plan '{envelope.Id}' review digest no longer matches the pending plan.";
        }

        return null;
    }

    private static bool SameDigest(ApprovalDigest left, ApprovalDigest right)
    {
        return string.Equals(left.Algorithm, right.Algorithm, StringComparison.Ordinal) &&
               string.Equals(left.Canonicalization, right.Canonicalization, StringComparison.Ordinal) &&
               FixedTimeStringComparer.Equals(left.Value, right.Value);
    }

    private static bool SamePolicy(ApprovalPolicy left, ApprovalPolicy right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal);

    private static bool SameReusePolicy(ExecutionReusePolicy left, ExecutionReusePolicy right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal);

    private PlanEnvelope ToUntypedEnvelope<TPayload>(PlanEnvelope<TPayload> envelope)
    {
        var payload = JsonSerializer.SerializeToElement(envelope.Payload, jsonOptions);
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

    private sealed record EnvelopeReadResult(PlanEnvelope? Envelope, string Message)
    {
        public static EnvelopeReadResult Success(PlanEnvelope envelope) =>
            new(envelope, "Read.");

        public static EnvelopeReadResult Failed(string message) =>
            new(null, message);
    }
}
