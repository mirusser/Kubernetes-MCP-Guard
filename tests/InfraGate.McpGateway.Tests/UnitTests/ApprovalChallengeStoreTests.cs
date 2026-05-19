using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalChallengeStoreTests
{
    private static readonly ApprovalDigest IntentDigest = CreateDigest("intent");
    private static readonly ApprovalDigest ReviewDigest = CreateDigest("review");

    [Fact]
    public async Task GetAsync_WithNullChallengeId_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync(null!, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithEmptyChallengeId_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync(string.Empty, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithInvalidCharacters_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync("ABC!@#", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetAsync("abcdef1234", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindApprovedAsync_WhenDirectoryDoesNotExist_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.FindApprovedAsync("plan-1", "hash-1", "subject-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_PersistsChallengeAndReturnsItWithPendingStatus()
    {
        var store = CreateStore();

        var challenge = await store.CreateAsync(
            "plan-123",
            "hash-abc",
            "alice",
            "oauth-jwt",
            TimeSpan.FromMinutes(10),
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        Assert.NotEmpty(challenge.Id);
        Assert.Equal("plan-123", challenge.PlanId);
        Assert.Equal("hash-abc", challenge.PendingPlanHash);
        Assert.Equal("alice", challenge.RequesterSubject);
        Assert.Equal("oauth-jwt", challenge.RequesterAuthenticationType);
        Assert.Equal(IntentDigest, challenge.IntentDigest);
        Assert.Equal(ReviewDigest, challenge.ReviewDigest);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Pending, challenge.Status);
        Assert.True(challenge.ExpiresAtUtc > challenge.CreatedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_PersistsPendingPlanHashName()
    {
        var store = CreateStore();

        await store.CreateAsync(
            "plan-123",
            "hash-abc",
            "alice",
            "oauth-jwt",
            TimeSpan.FromMinutes(10),
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        var path = Directory.EnumerateFiles(store.ChallengeDirectory).Single();
        var json = await File.ReadAllTextAsync(path, CancellationToken.None);

        Assert.Contains("\"pendingPlanHash\"", json);
        Assert.Contains("\"intentDigest\"", json);
        Assert.Contains("\"reviewDigest\"", json);
        Assert.DoesNotContain("\"planHash\"", json);
    }

    [Fact]
    public async Task GetAsync_AfterCreate_ReturnsDeserializedChallenge()
    {
        var store = CreateStore();

        var created = await store.CreateAsync(
            "plan-456",
            "hash-def",
            "bob",
            null,
            TimeSpan.FromMinutes(5),
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        var fetched = await store.GetAsync(created.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("plan-456", fetched.PlanId);
        Assert.Equal("bob", fetched.RequesterSubject);
        Assert.Equal(ApprovalConventions.ChallengeStatuses.Pending, fetched.Status);
    }

    [Fact]
    public async Task FindApprovedAsync_WhenApprovedChallengeMatchesAllCriteria_ReturnsIt()
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc1122",
            PlanId: "plan-789",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: "oauth-jwt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            Status: ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject: "carol",
            DecidedAtUtc: DateTimeOffset.UtcNow,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindApprovedAsync("plan-789", "hash-xyz", "carol", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("aabbcc1122", found.Id);
    }

    [Fact]
    public async Task FindApprovedAsync_WhenChallengeIsPending_ReturnsNull()
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc9988",
            PlanId: "plan-789",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            Status: ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindApprovedAsync("plan-789", "hash-xyz", "carol", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindPendingAsync_WhenPendingChallengeMatchesAllCriteria_ReturnsIt()
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc8844",
            PlanId: "plan-789",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: "oauth-jwt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            Status: ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindPendingAsync(
            "plan-789",
            "hash-xyz",
            "carol",
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("aabbcc8844", found.Id);
    }

    [Theory]
    [InlineData(ApprovalConventions.ChallengeStatuses.Approved)]
    [InlineData(ApprovalConventions.ChallengeStatuses.Denied)]
    public async Task FindPendingAsync_WhenChallengeIsTerminal_ReturnsNull(string status)
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc8845",
            PlanId: "plan-789",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: "oauth-jwt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            Status: status,
            ApproverSubject: status == ApprovalConventions.ChallengeStatuses.Approved ? "carol" : null,
            DecidedAtUtc: DateTimeOffset.UtcNow,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindPendingAsync(
            "plan-789",
            "hash-xyz",
            "carol",
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindPendingAsync_WhenChallengeExpired_ReturnsNull()
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc8846",
            PlanId: "plan-789",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: "oauth-jwt",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-20),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            Status: ApprovalConventions.ChallengeStatuses.Pending,
            ApproverSubject: null,
            DecidedAtUtc: null,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindPendingAsync(
            "plan-789",
            "hash-xyz",
            "carol",
            IntentDigest,
            ReviewDigest,
            CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindApprovedAsync_WhenPlanIdDoesNotMatch_ReturnsNull()
    {
        var store = CreateStore();
        var challenge = new ApprovalChallenge(
            Id: "aabbcc7766",
            PlanId: "plan-other",
            PendingPlanHash: "hash-xyz",
            RequesterSubject: "carol",
            RequesterAuthenticationType: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
            Status: ApprovalConventions.ChallengeStatuses.Approved,
            ApproverSubject: "carol",
            DecidedAtUtc: DateTimeOffset.UtcNow,
            IntentDigest: IntentDigest,
            ReviewDigest: ReviewDigest);

        await store.SaveAsync(challenge, CancellationToken.None);

        var found = await store.FindApprovedAsync("plan-789", "hash-xyz", "carol", CancellationToken.None);

        Assert.Null(found);
    }

    private static ApprovalChallengeStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-challenge-tests", Guid.NewGuid().ToString("N"));
        var options = new ApprovalStoreOptions(root);

        return new ApprovalChallengeStore(options);
    }

    private static ApprovalDigest CreateDigest(string value) =>
        new(ApprovalConventions.Digests.Sha256, "test.canonicalization.v1", value);
}
