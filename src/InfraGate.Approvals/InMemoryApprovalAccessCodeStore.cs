using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace InfraGate.Approvals;

public sealed class InMemoryApprovalAccessCodeStore : IApprovalAccessCodeStore
{
    private static readonly Meter Meter = new("InfraGate.Approvals", "1.0");
    private static readonly Counter<long> ExpiredCodeCounter =
        Meter.CreateCounter<long>("infragate.gateway.code.expired");

    private readonly ConcurrentDictionary<string, AccessCodeEntry> entries = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    public InMemoryApprovalAccessCodeStore(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ApprovalAccessCode> GenerateAsync(
        string challengeId,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Approval access code TTL must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string code;
        do
        {
            code = ApprovalAccessCodeGenerator.Generate();
        }
        while (!entries.TryAdd(
            code,
            new AccessCodeEntry(challengeId, timeProvider.GetUtcNow().Add(ttl), ConsumedAtUtc: null)));

        var entry = entries[code];
        return Task.FromResult(new ApprovalAccessCode(code, challengeId, entry.ExpiresAtUtc));
    }

    public Task<ApprovalAccessCodeConsumeResult> ConsumeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = Normalize(code);
        if (normalized is null || !entries.TryGetValue(normalized, out var entry))
        {
            return Task.FromResult(ApprovalAccessCodeConsumeResult.Invalid());
        }

        while (true)
        {
            var now = timeProvider.GetUtcNow();
            if (entry.ExpiresAtUtc <= now)
            {
                ExpiredCodeCounter.Add(1);
                return Task.FromResult(ApprovalAccessCodeConsumeResult.Expired());
            }

            if (entry.ConsumedAtUtc is not null)
            {
                return Task.FromResult(ApprovalAccessCodeConsumeResult.Consumed());
            }

            var consumed = entry with { ConsumedAtUtc = now };
            if (entries.TryUpdate(normalized, consumed, entry))
            {
                return Task.FromResult(ApprovalAccessCodeConsumeResult.Success(entry.ChallengeId));
            }

            if (!entries.TryGetValue(normalized, out entry))
            {
                return Task.FromResult(ApprovalAccessCodeConsumeResult.Invalid());
            }
        }
    }

    private static string? Normalize(string code)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return normalized.Length == ApprovalConventions.AccessCodes.CodeLength &&
               normalized.All(c => ApprovalConventions.AccessCodes.Alphabet.Contains(c, StringComparison.Ordinal))
            ? normalized
            : null;
    }

    private sealed record class AccessCodeEntry(
        string ChallengeId,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ConsumedAtUtc);
}
