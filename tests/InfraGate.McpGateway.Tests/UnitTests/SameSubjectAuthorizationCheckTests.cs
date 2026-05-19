using InfraGate.Approvals;
using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class SameSubjectAuthorizationCheckTests
{
    [Fact]
    public async Task EvaluateAsync_WhenSubjectsMatch_ReturnsAuthorized()
    {
        var check = new SameSubjectAuthorizationCheck();
        var context = new PlanAuthorizationContext("user-a", "user-a");

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.IsAuthorized);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSubjectsDiffer_ReturnsDeniedWithReason()
    {
        var check = new SameSubjectAuthorizationCheck();
        var context = new PlanAuthorizationContext("user-a", "user-b");

        var result = await check.EvaluateAsync(context, CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.NotNull(result.Reason);
        Assert.NotEmpty(result.Reason);
    }
}
