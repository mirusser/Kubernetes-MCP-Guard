using System.Security.Cryptography;
using System.Text.Json;

namespace InfraGate.Approvals;

public sealed class ApprovalChallengeStore
{
    private const int ChallengeIdByteCount = 32;

    private readonly ApprovalStoreOptions options;
    private readonly SemaphoreSlim storeLock = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public ApprovalChallengeStore(ApprovalStoreOptions options)
    {
        this.options = options;
    }

    public string ChallengeDirectory => Path.Combine(options.ApprovalRoot, ApprovalConventions.Storage.ChallengesDirectory);

    public Task<ApprovalChallenge> CreateAsync(
        string planId,
        string planHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        CancellationToken cancellationToken) =>
        CreateAsync(
            planId,
            planHash,
            requesterSubject,
            requesterAuthenticationType,
            ttl,
            intentDigest: null,
            reviewDigest: null,
            cancellationToken);

    public async Task<ApprovalChallenge> CreateAsync(
        string planId,
        string planHash,
        string requesterSubject,
        string? requesterAuthenticationType,
        TimeSpan ttl,
        ApprovalDigest? intentDigest,
        ApprovalDigest? reviewDigest,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = new ApprovalChallenge(
            NewChallengeId(),
            planId,
            planHash,
            requesterSubject,
            requesterAuthenticationType,
            now,
            now.Add(ttl),
            ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            intentDigest,
            reviewDigest);

        await SaveAsync(challenge, cancellationToken);

        return challenge;
    }

    public async Task<ApprovalChallenge?> GetAsync(string challengeId, CancellationToken cancellationToken)
    {
        if (!IsSafeChallengeId(challengeId))
        {
            return null;
        }

        var path = GetChallengePath(challengeId);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);

        return JsonSerializer.Deserialize<ApprovalChallenge>(json, jsonOptions);
    }

    public async Task<ApprovalChallenge?> FindApprovedAsync(
        string planId,
        string planHash,
        string subject,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(ChallengeDirectory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(
                     ChallengeDirectory,
                     "*" + ApprovalConventions.Storage.JsonExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var challenge = JsonSerializer.Deserialize<ApprovalChallenge>(json, jsonOptions);
            if (challenge is not null &&
                IsApprovedForSubject(challenge, planId, planHash, subject))
            {
                return challenge;
            }
        }

        return null;
    }

    public async Task SaveAsync(ApprovalChallenge challenge, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ChallengeDirectory);
        await storeLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(challenge, jsonOptions);
            await File.WriteAllTextAsync(GetChallengePath(challenge.Id), json, cancellationToken);
        }
        finally
        {
            storeLock.Release();
        }
    }

    private string GetChallengePath(string challengeId) =>
        Path.Combine(ChallengeDirectory, challengeId + ApprovalConventions.Storage.JsonExtension);

    private static string NewChallengeId()
    {
        var bytes = new byte[ChallengeIdByteCount];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsSafeChallengeId(string challengeId)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
        {
            return false;
        }

        return challengeId.All(c => c is >= 'a' and <= 'z' || c is >= '0' and <= '9');
    }

    private static bool IsApprovedForSubject(
        ApprovalChallenge challenge,
        string planId,
        string planHash,
        string subject)
    {
        return string.Equals(challenge.Status, ApprovalConventions.ChallengeStatuses.Approved, StringComparison.Ordinal) &&
               string.Equals(challenge.PlanId, planId, StringComparison.Ordinal) &&
               FixedTimeStringComparer.Equals(challenge.PlanHash, planHash) &&
               string.Equals(challenge.RequesterSubject, subject, StringComparison.Ordinal) &&
               string.Equals(challenge.ApproverSubject, subject, StringComparison.Ordinal);
    }
}
