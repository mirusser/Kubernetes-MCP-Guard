using System.Text.Json;

namespace InfraGate.McpGateway;

internal static class ToolArgumentConverter
{
    internal const int WaitForPlanApprovalDefaultTimeoutSeconds = 55;
    internal const int WaitForPlanApprovalMinimumTimeoutSeconds = 1;
    internal const int WaitForPlanApprovalMaximumTimeoutSeconds = 300;

    internal static IReadOnlyDictionary<string, object?> ConvertArguments(IDictionary<string, JsonElement>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object?>(args.Count, StringComparer.Ordinal);
        foreach (var (key, element) in args)
        {
            result[key] = JsonElementToObject(element);
        }

        return result;
    }

    internal static IReadOnlyDictionary<string, object?> ConvertObjectArguments(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonElementToObject(property.Value);
        }

        return result;
    }

    internal static object? JsonElementToObject(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element,
            _ => element
        };

    internal static bool TryGetWaitTimeoutSeconds(
        IReadOnlyDictionary<string, object?> args,
        out int timeoutSeconds,
        out string timeoutError)
    {
        timeoutSeconds = WaitForPlanApprovalDefaultTimeoutSeconds;
        timeoutError = string.Empty;

        if (!args.TryGetValue(McpGatewayConventions.ToolArguments.TimeoutSeconds, out var timeoutObj))
        {
            return true;
        }

        if (timeoutObj is int timeout)
        {
            timeoutSeconds = timeout;
        }
        else if (timeoutObj is double doubleTimeout &&
                 double.IsInteger(doubleTimeout) && // NOSONAR:S1244 — intended API, not an equality comparison
                 doubleTimeout >= int.MinValue &&
                 doubleTimeout <= int.MaxValue)
        {
            timeoutSeconds = (int)doubleTimeout;
        }
        else
        {
            timeoutError = McpGatewayMessages.ArgumentValidation.TimeoutMustBeInteger;
            return false;
        }

        if (timeoutSeconds is < WaitForPlanApprovalMinimumTimeoutSeconds or > WaitForPlanApprovalMaximumTimeoutSeconds)
        {
            timeoutError = McpGatewayMessages.ArgumentValidation.TimeoutMustBeInteger;
            return false;
        }

        return true;
    }
}
