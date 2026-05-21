using InfraGate.DownstreamAuth;

namespace InfraGate.McpGateway.Tests.UnitTests.DownstreamAuth;

/// <summary>
/// Tests that verify the gateway can access and use the DownstreamAuthConventions error code constant.
/// This ensures Task 7 (retry detection) can reliably reference the constant.
/// </summary>
public sealed class DownstreamAuthErrorCodesTests
{
    [Fact]
    public void ErrorCodeConstant_IsAccessibleFromGateway()
    {
        // This test verifies that the gateway project can reference the error code constant
        // from the InfraGate.DownstreamAuth project.
        string errorCode = DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired;

        Assert.Equal("downstream_auth_required", errorCode);
    }

    [Fact]
    public void ErrorCodeConstant_MatchesServerSideValue()
    {
        // Verify the constant value matches what the server-side filter will use
        // so retry detection logic can match exactly.
        Assert.Equal(
            "downstream_auth_required",
            DownstreamAuthConventions.ErrorCodes.DownstreamAuthRequired);
    }
}
