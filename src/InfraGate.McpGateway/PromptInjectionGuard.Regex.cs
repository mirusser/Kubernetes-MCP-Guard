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
}
