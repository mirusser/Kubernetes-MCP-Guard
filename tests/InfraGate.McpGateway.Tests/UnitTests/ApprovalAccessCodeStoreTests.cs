using InfraGate.Approvals;
using InfraGate.Approvals.AccessCodes;

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
        Assert.Equal(ApprovalConventions.AccessCodes.ConsumeResultReasonCodes.Consumed, second.ReasonCode);
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
        Assert.Equal(ApprovalConventions.AccessCodes.ConsumeResultReasonCodes.Expired, result.ReasonCode);
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

    [Fact]
    public async Task GenerateAsync_EmptyChallengeId_ThrowsArgumentException()
    {
        var store = new InMemoryApprovalAccessCodeStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GenerateAsync(string.Empty, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ZeroTtl_ThrowsArgumentOutOfRangeException()
    {
        var store = new InMemoryApprovalAccessCodeStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GenerateAsync(ChallengeId, TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_NegativeTtl_ThrowsArgumentOutOfRangeException()
    {
        var store = new InMemoryApprovalAccessCodeStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GenerateAsync(ChallengeId, TimeSpan.FromSeconds(-1), CancellationToken.None));
    }

    [Fact]
    public async Task ConsumeAsync_UnknownCode_ReturnsInvalid()
    {
        var store = new InMemoryApprovalAccessCodeStore();

        var result = await store.ConsumeAsync("ABCDEFGH", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.AccessCodes.ConsumeResultReasonCodes.Invalid, result.ReasonCode);
    }

    [Theory]
    [InlineData("not-valid!")]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("ABCDEFGHI")]
    public async Task ConsumeAsync_InvalidCodeFormat_ReturnsInvalid(string code)
    {
        var store = new InMemoryApprovalAccessCodeStore();

        var result = await store.ConsumeAsync(code, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApprovalConventions.AccessCodes.ConsumeResultReasonCodes.Invalid, result.ReasonCode);
    }

    [Fact]
    public async Task ConsumeAsync_ValidCodeSucceeds_ReturnsCorrectChallengeId()
    {
        var store = new InMemoryApprovalAccessCodeStore();
        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await store.ConsumeAsync(code.Code, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ChallengeId, result.ChallengeId);
    }

    [Fact]
    public async Task ConsumeAsync_CodeWithLeadingTrailingWhitespace_NormalizesAndSucceeds()
    {
        var store = new InMemoryApprovalAccessCodeStore();
        var code = await store.GenerateAsync(ChallengeId, TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await store.ConsumeAsync($"  {code.Code}  ", CancellationToken.None);

        Assert.True(result.Succeeded);
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
