using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalAccessCodeStoreTests
{
    private const string ChallengeId = "challenge-1";

    [Fact]
    public async Task GenerateAsync_ReturnsEightCharacterCodeFromConstrainedAlphabet()
    {
        var store = new InMemoryApprovalAccessCodeStore();

        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(ApprovalConventions.AccessCodes.CodeLength, code.Code.Length);
        Assert.All(code.Code, c => Assert.Contains(c, ApprovalConventions.AccessCodes.Alphabet));
        Assert.DoesNotContain(code.Code, c => "0OIL1".Contains(c, StringComparison.Ordinal));
        Assert.Equal(ChallengeId, code.ChallengeId);
    }

    [Fact]
    public async Task ConsumeAsync_SameCodeTwice_ReturnsOneSuccess()
    {
        var store = new InMemoryApprovalAccessCodeStore();
        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(5), CancellationToken.None);

        var first = await store.ConsumeAsync(code.Code, CancellationToken.None);
        var second = await store.ConsumeAsync(code.Code, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal(ChallengeId, first.ChallengeId);
        Assert.False(second.Succeeded);
        Assert.Equal(ApprovalConventions.AccessCodes.ResultReasonCodes.Consumed, second.ReasonCode);
    }

    [Fact]
    public async Task ConsumeAsync_ExpiredCode_ReturnsExpired()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
        var store = new InMemoryApprovalAccessCodeStore(time);
        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(1), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(2));
        var result = await store.ConsumeAsync(code.Code, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.AccessCodes.ResultReasonCodes.Expired, result.ReasonCode);
        Assert.Null(result.ChallengeId);
    }

    [Fact]
    public async Task ConsumeAsync_ConcurrentCalls_ReturnExactlyOneSuccess()
    {
        var store = new InMemoryApprovalAccessCodeStore();
        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(5), CancellationToken.None);

        var tasks = Enumerable.Range(0, 64)
            .Select(_ => store.ConsumeAsync(code.Code, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.Succeeded));
        Assert.Equal(63, results.Count(result => !result.Succeeded));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan delta)
        {
            current = current.Add(delta);
        }
    }
}
