using System.Security.Cryptography;
using System.Text.Json;
using InfraGate.Approvals.AuditPayloads;

namespace InfraGate.Approvals;

public sealed class ApprovalStore
{
    private const int PlanIdRandomByteCount = 4;

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
            new PlanRequestedPayload(envelope.Id, envelope.Operation, targetNamespace, hash),
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
            string.IsNullOrWhiteSpace(envelope.Operation) ||
            string.IsNullOrWhiteSpace(envelope.Requester.Subject) ||
            envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return $"Plan '{planId}' uses an old approval file format. Re-request the plan.";
        }

        return null;
    }

    private PlanEnvelope ToUntypedEnvelope<TPayload>(PlanEnvelope<TPayload> envelope)
    {
        var payload = JsonSerializer.SerializeToElement(envelope.Payload, jsonOptions);
        return new PlanEnvelope(
            envelope.Id,
            envelope.AdapterId,
            envelope.Operation,
            envelope.CreatedAtUtc,
            envelope.Requester,
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
