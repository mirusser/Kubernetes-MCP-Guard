using InfraGate.Approvals;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class ApprovalChallengeStoreTests
{
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

    private static ApprovalChallengeStore CreateStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "infra-gate-challenge-tests", Guid.NewGuid().ToString("N"));
        var options = new ApprovalStoreOptions(root);

        return new ApprovalChallengeStore(options);
    }
}
