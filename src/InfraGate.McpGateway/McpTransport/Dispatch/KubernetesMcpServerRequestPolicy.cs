using System.Collections.Frozen;

namespace InfraGate.McpGateway;

internal sealed class KubernetesMcpServerRequestPolicy
{
    private const string NamespaceArgument = "namespace";
    private const string NameArgument = "name";
    private const string LabelSelectorArgument = "labelSelector";
    private const string FieldSelectorArgument = "fieldSelector";
    private const string ContainerArgument = "container";
    private const string TailArgument = "tail";
    private const string PreviousArgument = "previous";
    private const int MinimumLogTailLines = 0;

    private static readonly IReadOnlySet<string> PodsListArguments = new[]
    {
        NamespaceArgument,
        LabelSelectorArgument,
        FieldSelectorArgument
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PodsGetArguments = new[]
    {
        NamespaceArgument,
        NameArgument
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PodsLogArguments = new[]
    {
        NamespaceArgument,
        NameArgument,
        ContainerArgument,
        TailArgument,
        PreviousArgument
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IReadOnlySet<string> allowedNamespaces;

    internal KubernetesMcpServerRequestPolicy(IReadOnlySet<string> allowedNamespaces)
    {
        ArgumentNullException.ThrowIfNull(allowedNamespaces);
        if (allowedNamespaces.Count == 0)
        {
            throw new ArgumentException("At least one allowed namespace is required.", nameof(allowedNamespaces));
        }

        this.allowedNamespaces = allowedNamespaces.ToFrozenSet(StringComparer.Ordinal);
    }

    internal bool IsToolAllowed(string toolName) =>
        McpGatewayConventions.SecondaryDownstream.ApprovedTools.Contains(toolName);

    internal bool TryValidate(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        IReadOnlySet<string>? allowedArguments = GetAllowedArguments(toolName);
        if (allowedArguments is null)
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.ToolNotAllowed(toolName);
            return false;
        }

        string? unknownArgument = arguments.Keys.FirstOrDefault(argument => !allowedArguments.Contains(argument));
        if (unknownArgument is not null)
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.UnknownArgument(toolName, unknownArgument);
            return false;
        }

        if (!TryGetRequiredString(arguments, NamespaceArgument, out string namespaceName))
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.MissingOrInvalidArgument(
                toolName,
                NamespaceArgument);
            return false;
        }

        if (!allowedNamespaces.Contains(namespaceName))
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.NamespaceNotAllowed(namespaceName);
            return false;
        }

        return toolName switch
        {
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool =>
                TryValidateOptionalString(arguments, toolName, LabelSelectorArgument, out error)
                && TryValidateOptionalString(arguments, toolName, FieldSelectorArgument, out error),
            McpGatewayConventions.SecondaryDownstream.PodsGetTool =>
                TryValidateRequiredString(arguments, toolName, NameArgument, out error),
            McpGatewayConventions.SecondaryDownstream.PodsLogTool =>
                TryValidatePodLog(arguments, toolName, out error),
            _ => throw new InvalidOperationException("Approved Kubernetes MCP tool has no argument policy.")
        };
    }

    private static IReadOnlySet<string>? GetAllowedArguments(string toolName) =>
        toolName switch
        {
            McpGatewayConventions.SecondaryDownstream.PodsListInNamespaceTool => PodsListArguments,
            McpGatewayConventions.SecondaryDownstream.PodsGetTool => PodsGetArguments,
            McpGatewayConventions.SecondaryDownstream.PodsLogTool => PodsLogArguments,
            _ => null
        };

    private static bool TryValidatePodLog(
        IReadOnlyDictionary<string, object?> arguments,
        string toolName,
        out string error)
    {
        if (!TryValidateRequiredString(arguments, toolName, NameArgument, out error)
            || !TryValidateOptionalString(arguments, toolName, ContainerArgument, out error))
        {
            return false;
        }

        if (!arguments.TryGetValue(TailArgument, out object? tailValue)
            || !TryGetInteger(tailValue, out int tail)
            || tail is < MinimumLogTailLines or > McpGatewayConventions.DefaultLogTailLines)
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.LogTailOutOfRange;
            return false;
        }

        if (arguments.TryGetValue(PreviousArgument, out object? previous) && previous is not bool)
        {
            error = McpGatewayMessages.KubernetesMcpServerPolicy.MissingOrInvalidArgument(
                toolName,
                PreviousArgument);
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateRequiredString(
        IReadOnlyDictionary<string, object?> arguments,
        string toolName,
        string argumentName,
        out string error)
    {
        if (TryGetRequiredString(arguments, argumentName, out _))
        {
            error = string.Empty;
            return true;
        }

        error = McpGatewayMessages.KubernetesMcpServerPolicy.MissingOrInvalidArgument(toolName, argumentName);
        return false;
    }

    private static bool TryValidateOptionalString(
        IReadOnlyDictionary<string, object?> arguments,
        string toolName,
        string argumentName,
        out string error)
    {
        if (!arguments.TryGetValue(argumentName, out object? value) || value is string)
        {
            error = string.Empty;
            return true;
        }

        error = McpGatewayMessages.KubernetesMcpServerPolicy.MissingOrInvalidArgument(toolName, argumentName);
        return false;
    }

    private static bool TryGetRequiredString(
        IReadOnlyDictionary<string, object?> arguments,
        string argumentName,
        out string value)
    {
        if (arguments.TryGetValue(argumentName, out object? argumentValue)
            && argumentValue is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInteger(object? value, out int integer)
    {
        switch (value)
        {
            case int intValue:
                integer = intValue;
                return true;
            case double doubleValue when
                double.IsInteger(doubleValue) &&
                doubleValue is >= int.MinValue and <= int.MaxValue:
                integer = (int)doubleValue;
                return true;
            default:
                integer = default;
                return false;
        }
    }
}
