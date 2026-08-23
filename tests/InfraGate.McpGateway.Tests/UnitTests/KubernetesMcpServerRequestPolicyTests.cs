using InfraGate.McpGateway;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class KubernetesMcpServerRequestPolicyTests
{
    private const string AllowedNamespace = "mcp-nginx-demo";

    private readonly KubernetesMcpServerRequestPolicy policy = new(
        new HashSet<string>(StringComparer.Ordinal) { AllowedNamespace });

    [Theory]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool)]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsGetTool)]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsLogTool)]
    public void IsToolAllowed_ApprovedExactName_ReturnsTrue(string toolName)
    {
        Assert.True(policy.IsToolAllowed(toolName));
    }

    [Theory]
    [InlineData("pods_list")]
    [InlineData("events_list")]
    [InlineData("resources_get")]
    [InlineData("resources_list")]
    [InlineData("Pods_Get")]
    public void IsToolAllowed_UnapprovedName_ReturnsFalse(string toolName)
    {
        Assert.False(policy.IsToolAllowed(toolName));
    }

    [Theory]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool)]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsGetTool)]
    [InlineData(McpGatewayConventions.SecondaryDownstream.PodsLogTool)]
    public void TryValidate_ValidArguments_ReturnsTrue(string toolName)
    {
        IReadOnlyDictionary<string, object?> arguments = CreateValidArguments(toolName);

        bool isValid = policy.TryValidate(toolName, arguments, out string error);

        Assert.True(isValid, error);
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidate_IntegralDoubleLogTail_ReturnsTrue()
    {
        IReadOnlyDictionary<string, object?> arguments = Arguments(
            ("namespace", AllowedNamespace),
            ("name", "demo"),
            ("tail", 200.0));

        bool isValid = policy.TryValidate(
            McpGatewayConventions.SecondaryDownstream.PodsLogTool,
            arguments,
            out string error);

        Assert.True(isValid, error);
    }

    [Theory]
    [InlineData("unknownTool")]
    [InlineData("clusterWideList")]
    [InlineData("rawSecret")]
    [InlineData("rawConfigMap")]
    [InlineData("missingNamespace")]
    [InlineData("namespaceEscape")]
    [InlineData("contextEscape")]
    [InlineData("unknownArgument")]
    [InlineData("missingName")]
    [InlineData("missingTail")]
    [InlineData("tailAboveMaximum")]
    [InlineData("tailBelowMinimum")]
    [InlineData("tailNotInteger")]
    [InlineData("previousNotBoolean")]
    [InlineData("selectorNotString")]
    [InlineData("containerNotString")]
    [InlineData("eventsWithoutNamespace")]
    public void TryValidate_UnsafeRequest_ReturnsFalse(string scenario)
    {
        (string toolName, IReadOnlyDictionary<string, object?> arguments) = CreateUnsafeRequest(scenario);

        bool isValid = policy.TryValidate(toolName, arguments, out string error);

        Assert.False(isValid);
        Assert.NotEmpty(error);
    }

    private static IReadOnlyDictionary<string, object?> CreateValidArguments(string toolName) =>
        toolName switch
        {
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool =>
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["namespace"] = AllowedNamespace,
                    ["labelSelector"] = "app=nginx",
                    ["fieldSelector"] = "status.phase=Running"
                },
            McpGatewayConventions.SecondaryDownstream.PodsGetTool =>
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["namespace"] = AllowedNamespace,
                    ["name"] = "demo"
                },
            McpGatewayConventions.SecondaryDownstream.PodsLogTool =>
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["namespace"] = AllowedNamespace,
                    ["name"] = "demo",
                    ["container"] = "nginx",
                    ["tail"] = 200,
                    ["previous"] = false
                },
            _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null)
        };

    private static (string ToolName, IReadOnlyDictionary<string, object?> Arguments) CreateUnsafeRequest(
        string scenario) =>
        scenario switch
        {
            "unknownTool" => ("unknown_raw", EmptyArguments()),
            "clusterWideList" => ("pods_list", EmptyArguments()),
            "rawSecret" => ("resources_get", Arguments(("namespace", AllowedNamespace), ("kind", "Secret"))),
            "rawConfigMap" => ("resources_get", Arguments(("namespace", AllowedNamespace), ("kind", "ConfigMap"))),
            "missingNamespace" => (McpGatewayConventions.SecondaryDownstream.PodsGetTool,
                Arguments(("name", "demo"))),
            "namespaceEscape" => (McpGatewayConventions.SecondaryDownstream.PodsGetTool,
                Arguments(("namespace", "kube-system"), ("name", "demo"))),
            "contextEscape" => (McpGatewayConventions.SecondaryDownstream.PodsGetTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("context", "other"))),
            "unknownArgument" => (McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
                Arguments(("namespace", AllowedNamespace), ("limit", 500))),
            "missingName" => (McpGatewayConventions.SecondaryDownstream.PodsGetTool,
                Arguments(("namespace", AllowedNamespace))),
            "missingTail" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"))),
            "tailAboveMaximum" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("tail", 201))),
            "tailBelowMinimum" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("tail", -1))),
            "tailNotInteger" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("tail", 12.5))),
            "previousNotBoolean" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("tail", 20), ("previous", "false"))),
            "selectorNotString" => (McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool,
                Arguments(("namespace", AllowedNamespace), ("labelSelector", 42))),
            "containerNotString" => (McpGatewayConventions.SecondaryDownstream.PodsLogTool,
                Arguments(("namespace", AllowedNamespace), ("name", "demo"), ("tail", 20), ("container", false))),
            "eventsWithoutNamespace" => ("events_list",
                Arguments(("fieldSelector", "involvedObject.name=demo"))),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

    private static IReadOnlyDictionary<string, object?> EmptyArguments() =>
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> Arguments(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
}
