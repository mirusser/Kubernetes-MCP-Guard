using InfraGate.DevIssuer;
using InfraGate.RuntimeSafety;

namespace InfraGate.DevIssuer.Tests.UnitTests;

public sealed class DevIssuerOptionsTests
{
    [Fact]
    public void FromEnvironment_UsesDefaults_WhenUnset()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, null),
            (DevIssuerConventions.EnvironmentVariables.Issuer, null),
            (DevIssuerConventions.EnvironmentVariables.Resource, null),
            (DevIssuerConventions.EnvironmentVariables.Scope, null),
            (DevIssuerConventions.EnvironmentVariables.Subject, null),
            (DevIssuerConventions.EnvironmentVariables.InternalEndpointBase, null),
            (DevIssuerConventions.EnvironmentVariables.ApprovalClientId, null),
            (DevIssuerConventions.EnvironmentVariables.ApprovalRedirectUri, null));

        var options = DevIssuerOptions.FromEnvironment();

        Assert.Equal(DevIssuerConventions.DefaultUrl, options.Issuer);
        Assert.Equal(DevIssuerConventions.DefaultResource, options.Resource);
        Assert.Equal(DevIssuerConventions.DefaultScope, options.Scope);
        Assert.Equal(DevIssuerConventions.DefaultSubject, options.Subject);
        Assert.Null(options.InternalEndpointBase);
        Assert.Equal(DevIssuerConventions.DefaultApprovalClientId, options.ApprovalClientId);
        Assert.Equal(DevIssuerConventions.DefaultApprovalRedirectUri, options.ApprovalRedirectUri);
    }

    [Fact]
    public void FromEnvironment_UsesConfiguredValues_WhenSet()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, null),
            (DevIssuerConventions.EnvironmentVariables.Issuer, "http://127.0.0.1:4011"),
            (DevIssuerConventions.EnvironmentVariables.Resource, "http://127.0.0.1:4001/mcp"),
            (DevIssuerConventions.EnvironmentVariables.Scope, "mcp:tools mcp:admin"),
            (DevIssuerConventions.EnvironmentVariables.Subject, "dev-user"),
            (DevIssuerConventions.EnvironmentVariables.InternalEndpointBase, "http://devissuer:3011"),
            (DevIssuerConventions.EnvironmentVariables.ApprovalClientId, "approval-client"),
            (DevIssuerConventions.EnvironmentVariables.ApprovalRedirectUri, "http://127.0.0.1:4001/approvals/oauth/callback"));

        var options = DevIssuerOptions.FromEnvironment();

        Assert.Equal("http://127.0.0.1:4011", options.Issuer);
        Assert.Equal("http://127.0.0.1:4001/mcp", options.Resource);
        Assert.Equal("mcp:tools mcp:admin", options.Scope);
        Assert.Equal("dev-user", options.Subject);
        Assert.Equal("http://devissuer:3011", options.InternalEndpointBase);
        Assert.Equal("approval-client", options.ApprovalClientId);
        Assert.Equal("http://127.0.0.1:4001/approvals/oauth/callback", options.ApprovalRedirectUri);
    }

    [Fact]
    public void ProductionMode_WithDevIssuer_RefusesStartup()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Production));

        var options = DevIssuerOptions.FromEnvironment();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.ValidateProductionSafety);

        Assert.Contains("development-only", exception.Message);
    }

    [Fact]
    public void DevelopmentMode_AllowsLocalDefaults()
    {
        using var environment = EnvironmentVariableScope.Set(
            (RuntimeSafetyConventions.EnvironmentVariables.InfraGateEnvironment, RuntimeSafetyConventions.EnvironmentValues.Development),
            (DevIssuerConventions.EnvironmentVariables.Issuer, null),
            (DevIssuerConventions.EnvironmentVariables.Resource, null),
            (DevIssuerConventions.EnvironmentVariables.Scope, null),
            (DevIssuerConventions.EnvironmentVariables.Subject, null),
            (DevIssuerConventions.EnvironmentVariables.InternalEndpointBase, null),
            (DevIssuerConventions.EnvironmentVariables.ApprovalClientId, null),
            (DevIssuerConventions.EnvironmentVariables.ApprovalRedirectUri, null));

        var options = DevIssuerOptions.FromEnvironment();
        Exception? exception = Record.Exception(options.ValidateProductionSafety);

        Assert.Null(exception);
        Assert.Equal(DevIssuerConventions.DefaultUrl, options.Issuer);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues;

        private EnvironmentVariableScope(Dictionary<string, string?> previousValues)
        {
            this.previousValues = previousValues;
        }

        public static EnvironmentVariableScope Set(params (string Name, string? Value)[] variables)
        {
            var previousValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var variable in variables)
            {
                previousValues[variable.Name] = Environment.GetEnvironmentVariable(variable.Name);
                Environment.SetEnvironmentVariable(variable.Name, variable.Value);
            }

            return new EnvironmentVariableScope(previousValues);
        }

        public void Dispose()
        {
            foreach (var previousValue in previousValues)
            {
                Environment.SetEnvironmentVariable(previousValue.Key, previousValue.Value);
            }
        }
    }
}
