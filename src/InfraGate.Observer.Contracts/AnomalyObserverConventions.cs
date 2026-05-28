using System.Security.Cryptography;
using System.Text;

namespace InfraGate.Observer.Contracts;

public static class AnomalyObserverConventions
{
    public const int DefaultCadenceSeconds = 60;
    public const int MinCadenceSeconds = 10;
    public const int MaxCadenceSeconds = 3600;
    public const int WallClockCapSeconds = 120;
    public const int MinWallClockCapSeconds = 10;
    public const int MaxWallClockCapSeconds = 300;
    public const int MaxToolIterations = 8;
    public const int MinMaxToolIterations = 1;
    public const int MaxMaxToolIterations = 20;
    public const int DefaultDedupeSuppressionWindow = 5;
    public const int MinDedupeSuppressionWindow = 1;
    public const int MaxDedupeSuppressionWindow = 30;
    public const int DefaultDedupeResolutionThreshold = 2;
    public const int MinDedupeResolutionThreshold = 1;
    public const int MaxDedupeResolutionThreshold = 10;
    public const string DefaultLlmModel = "claude-sonnet-4-6";
    public const string DefaultOpenRouterLlmModel = "deepseek/deepseek-v4-flash:free";

    public static string ComputeAnomalyId(AnomalyKind kind, ResourceRef target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var input = $"{kind}|{target.ApiVersion}|{target.Kind}|{target.Namespace}|{target.Name}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes)[..12];
    }

    public static string ComputeAnomalyId(AnomalyKind kind, string apiVersion, string resourceKind, string namespaceName, string resourceName)
    {
        var input = $"{kind}|{apiVersion}|{resourceKind}|{namespaceName}|{resourceName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes)[..12];
    }
}
