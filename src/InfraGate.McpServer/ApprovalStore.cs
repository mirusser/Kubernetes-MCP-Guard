using System.Security.Cryptography;
using System.Text.Json;

namespace InfraGate.McpServer;

public sealed class ApprovalStore
{
    private readonly K8sMcpOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ApprovalStore(K8sMcpOptions options)
    {
        _options = options;
    }

    public string PendingDirectory => Path.Combine(_options.ApprovalRoot, "pending");

    public string ApprovedDirectory => Path.Combine(_options.ApprovalRoot, "approved");

    public string AppliedDirectory => Path.Combine(_options.ApprovalRoot, "applied");

    public string AuditPath => Path.Combine(_options.ApprovalRoot, "audit.jsonl");

    public static string NewPlanId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);

        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public async Task<ApprovalPlanResult> CreatePlanAsync(K8sPlan plan, CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var pendingPath = GetPendingPath(plan.Id);
        var json = JsonSerializer.Serialize(plan, _jsonOptions);
        await File.WriteAllTextAsync(pendingPath, json, cancellationToken);

        var hash = await ComputeSha256Async(pendingPath, cancellationToken);
        await WriteAuditAsync("plan_requested", new
        {
            plan.Id,
            plan.Operation,
            plan.Namespace,
            hash
        }, cancellationToken);

        return new ApprovalPlanResult(plan, pendingPath, GetApprovedPath(plan.Id), hash);
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

        if (!StringComparer.OrdinalIgnoreCase.Equals(actualHash, approvedHash))
        {
            await WriteAuditAsync("approval_hash_mismatch", new
            {
                planId,
                approvedHash,
                actualHash
            }, cancellationToken);

            return ApprovedPlanResult.Denied($"Plan '{planId}' changed after approval; refusing to apply it.");
        }

        var json = await File.ReadAllTextAsync(pendingPath, cancellationToken);
        var plan = JsonSerializer.Deserialize<K8sPlan>(json, _jsonOptions);

        return plan is null
            ? ApprovedPlanResult.Denied($"Plan '{planId}' could not be read.")
            : ApprovedPlanResult.Approved(plan, actualHash);
    }

    public async Task MarkAppliedAsync(K8sPlan plan, string hash, CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var appliedPath = GetAppliedPath(plan.Id);
        var json = JsonSerializer.Serialize(new
        {
            plan.Id,
            plan.Operation,
            plan.Namespace,
            hash,
            appliedAtUtc = DateTimeOffset.UtcNow
        }, _jsonOptions);

        await File.WriteAllTextAsync(appliedPath, json, cancellationToken);
        await WriteAuditAsync("plan_applied", new
        {
            plan.Id,
            plan.Operation,
            plan.Namespace,
            hash
        }, cancellationToken);
    }

    public Task WriteAuditAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        EnsureDirectories();

        var line = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            eventName,
            payload
        }, _jsonOptions);

        return File.AppendAllTextAsync(AuditPath, line + Environment.NewLine, cancellationToken);
    }

    public string GetPendingPath(string planId) => Path.Combine(PendingDirectory, $"{planId}.json");

    public string GetApprovedPath(string planId) => Path.Combine(ApprovedDirectory, $"{planId}.sha256");

    public string GetAppliedPath(string planId) => Path.Combine(AppliedDirectory, $"{planId}.json");

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
    }

    private static bool IsSafePlanId(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return false;
        }

        return planId.All(c => c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-');
    }
}

public sealed record ApprovalPlanResult(K8sPlan Plan, string PendingPath, string ApprovedPath, string Hash);

public sealed record ApprovedPlanResult(bool IsApproved, K8sPlan? Plan, string? Hash, string Message)
{
    public static ApprovedPlanResult Approved(K8sPlan plan, string hash) =>
        new(true, plan, hash, "Approved.");

    public static ApprovedPlanResult Denied(string message) =>
        new(false, null, null, message);
}
