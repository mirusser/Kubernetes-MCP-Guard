using System.Text;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

public static partial class PromptInjectionGuard
{
    private static void AddTextFindings(string text, string location, List<GuardrailFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach ((string? category, Regex? pattern) in Patterns)
        {
            if (pattern.IsMatch(text))
            {
                findings.Add(new GuardrailFinding(location, category));
            }
        }

        if (!TryScanBase64Payloads(text, location, findings))
        {
            TryScanEmbeddedBase64Payloads(text, location, findings);
        }
    }

    private static bool TryScanBase64Payloads(string text, string location, List<GuardrailFinding> findings)
    {
        if (text.Length < 20)
        {
            return false;
        }

        bool hasInvalidChar = false;
        foreach (char c in text)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '=')
            {
                continue;
            }

            hasInvalidChar = true;
            break;
        }

        if (hasInvalidChar)
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return false;
        }

        return ScanDecodedBase64(decoded, location, findings);
    }

    private static void TryScanEmbeddedBase64Payloads(string text, string location, List<GuardrailFinding> findings)
    {
        MatchCollection matches = EmbeddedBase64Regex().Matches(text);
        foreach (Match match in matches)
        {
            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(match.Value);
            }
            catch (FormatException)
            {
                // The regex matches substrings that look like base64 but may not be
                // decodable (e.g., wrong padding, invalid character sequence). Skip
                // this match and try the next one — the guardrail must never crash on
                // malformed input.
                continue;
            }

            ScanDecodedBase64(decoded, location, findings);
        }
    }

    private static bool ScanDecodedBase64(byte[] decoded, string location, List<GuardrailFinding> findings)
    {
        string decodedText;
        try
        {
            decodedText = Encoding.UTF8.GetString(decoded);
        }
        catch (ArgumentException)
        {
            return false;
        }

        // Justification: S3267 — .Count(predicate) is already the canonical LINQ form; there is no foreach+if loop to simplify with .Where().
        int printable = decodedText.Count(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t');

        if (printable < decodedText.Length * 0.7)
        {
            return false;
        }

        foreach ((string? category, Regex? pattern) in Patterns)
        {
            if (pattern.IsMatch(decodedText))
            {
                findings.Add(new GuardrailFinding(location, category));
            }
        }

        return true;
    }

    private static bool IsOperationalLine(string line) =>
        OperationalLineRegex().IsMatch(line);

    private static bool IsLineBreak(string line) =>
        line is "\r" or "\n" or "\r\n";

    [GeneratedRegex(
        @"(?ims)(?<prefix>^[ \t]*Manifest:\s*\r?\n)```(?:ya?ml)?\s*\r?\n(?<manifest>.*?)```+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex ManifestBlockRegex();

    [GeneratedRegex(@"(\r\n|\r|\n)", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex LineSplitRegex();

    [GeneratedRegex(
        OperationalLinePattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex OperationalLineRegex();

    [GeneratedRegex(
        @"^\s*(?:Pending file|Approval file|Plan hash):(?:\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex SensitivePlanMetadataLineRegex();

    [GeneratedRegex(@"[A-Za-z0-9+/]{20,}={0,2}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex EmbeddedBase64Regex();

    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget|override)\b.{0,80}\b(?:instructions?|rules?|prompts?|system prompt|developer prompt)\b|\b(?:instructions?|rules?|prompts?)\b.{0,80}\b(?:ignore|disregard|forget|override)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex IgnoreInstructionsRegex();

    [GeneratedRegex(
        @"\b(?:reveal|show|print|dump|display)\b.{0,80}\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b|\b(?:system|developer|hidden)\b.{0,80}\b(?:prompts?|instructions?|messages?)\b.{0,80}\b(?:reveal|show|print|dump|display)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex RevealPromptsRegex();

    [GeneratedRegex(ToolUsePattern,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex ToolUseRegex();

    [GeneratedRegex(
        @"\b(?:exfiltrate|leak|send|post|upload|copy)\b.{0,100}\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b|\b(?:secrets?|tokens?|passwords?|credentials?|kubeconfig|ssh|api\s*keys?|system prompt)\b.{0,100}\b(?:exfiltrate|leak|send|post|upload|copy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex SecretExfiltrationRegex();

    [GeneratedRegex(
        @"\b(?:system|developer|highest priority|authoritative)\b.{0,80}\b(?:instructions?|messages?|prompts?)\b|\byou are now\b|\bact as (?:system|developer)\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: McpGatewayConventions.RegexTimeoutMilliseconds)]
    private static partial Regex AuthorityOverrideRegex();
}
