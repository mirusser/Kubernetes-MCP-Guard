using System.Text;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

public sealed partial class PromptInjectionGuard
{
    private static void AddTextFindings(string text, string location, List<GuardrailFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var (category, pattern) in Patterns)
        {
            if (pattern.IsMatch(text))
            {
                findings.Add(new GuardrailFinding(location, category));
            }
        }

        TryScanBase64Payloads(text, location, findings);
    }

    private static void TryScanBase64Payloads(string text, string location, List<GuardrailFinding> findings)
    {
        if (text.Length < 20)
        {
            return;
        }

        var hasInvalidChar = false;
        foreach (char c in text)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/' or '=')
            {
                continue;
            }

            hasInvalidChar = true;
            break;
        }

        if (hasInvalidChar)
        {
            return;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return;
        }

        string decodedText;
        try
        {
            decodedText = Encoding.UTF8.GetString(decoded);
        }
        catch (ArgumentException)
        {
            return;
        }

        var printable = 0;
        foreach (char c in decodedText)
        {
            if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
            {
                printable++;
            }
        }

        if (printable < decodedText.Length * 0.7)
        {
            return;
        }

        foreach (var (category, pattern) in Patterns)
        {
            if (pattern.IsMatch(decodedText))
            {
                findings.Add(new GuardrailFinding(location, category));
            }
        }
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
}
