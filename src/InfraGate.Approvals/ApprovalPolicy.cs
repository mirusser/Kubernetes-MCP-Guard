using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace InfraGate.Approvals;

public sealed record class ApprovalPolicy
{
    public ApprovalPolicy()
        : this(string.Empty, parameters: null)
    {
    }

    public ApprovalPolicy(string type, IReadOnlyDictionary<string, string>? parameters = null)
    {
        Type = type;
        Parameters = NormalizeParameters(parameters);
    }

    public string Type { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    public static ApprovalPolicy SameSubject() =>
        new(ApprovalConventions.ApprovalPolicyTypes.SameSubject);

    public static ApprovalPolicy OperatorApproval(string operatorGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorGroup);

        return new ApprovalPolicy(
            ApprovalConventions.ApprovalPolicyTypes.OperatorApproval,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ApprovalConventions.ApprovalPolicyParameters.OperatorGroup] = operatorGroup
            });
    }

    public bool Equals([NotNullWhen(true)] ApprovalPolicy? other)
    {
        return other is not null &&
               string.Equals(Type, other.Type, StringComparison.Ordinal) &&
               SameParameters(Parameters, other.Parameters);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type, StringComparer.Ordinal);
        if (Parameters is not null)
        {
            foreach (var (key, value) in Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hash.Add(key, StringComparer.Ordinal);
                hash.Add(value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyDictionary<string, string>? NormalizeParameters(
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            result[key] = value;
        }

        return result;
    }

    private static bool SameParameters(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (left is null || left.Count == 0)
        {
            return right is null || right.Count == 0;
        }

        if (right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(leftValue, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
