using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace InfraGate.McpGateway;

/// <summary>
/// Result from sanitizing typed content blocks while preserving their structure.
/// </summary>
internal sealed record class SanitizedContentResult(
    IReadOnlyList<object> Content,
    bool IsError,
    JsonObject? Meta,
    IReadOnlyList<GuardrailFinding> Findings,
    bool ManifestRedacted)
{
    public bool HasFindings => Findings.Count > 0;
    public bool IsPolicyError { get; init; }
    public string? PolicyErrorMessage { get; init; }

    /// <summary>
    /// Creates a policy error result for unsupported or over-limit content.
    /// </summary>
    public static SanitizedContentResult CreatePolicyError(string errorMessage)
    {
        return new SanitizedContentResult(
            [new TextContentBlock { Text = $"[Policy Error] {errorMessage}" }],
            IsError: true,
            Meta: null,
            Findings: [],
            ManifestRedacted: false)
        {
            IsPolicyError = true,
            PolicyErrorMessage = errorMessage
        };
    }
}
