using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace InfraGate.McpGateway;

internal sealed class SensitiveDataRedactor
{
    private readonly IReadOnlyList<(RedactionPattern Pattern, Regex Regex)> patterns;
    private readonly ILogger<SensitiveDataRedactor> logger;

    internal SensitiveDataRedactor(IReadOnlyList<RedactionPattern> patterns, ILogger<SensitiveDataRedactor> logger)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(logger);

        this.patterns = patterns
            .Select(pattern => (pattern, Compile(pattern.Regex)))
            .ToArray();
        this.logger = logger;
    }

    public RedactionResult Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrEmpty(text) || patterns.Count == 0)
        {
            return new RedactionResult(text, false, FrozenDictionary<string, int>.Empty, []);
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = new List<string>();
        string current = text;
        bool wasRedacted = false;

        foreach ((RedactionPattern pattern, Regex regex) in patterns)
        {
            try
            {
                int count = 0;
                current = regex.Replace(
                    current,
                    match =>
                    {
                        count++;
                        return McpGatewayConventions.SensitiveDataRedaction.Placeholder(pattern.Name);
                    });

                if (count > 0)
                {
                    counts[pattern.Name] = count;
                    matched.Add(pattern.Name);
                    wasRedacted = true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                logger.LogWarning(
                    "Sensitive data redaction timed out for pattern '{PatternName}' after {TimeoutMs} ms; returning original text",
                    pattern.Name,
                    McpGatewayConventions.RegexTimeoutMilliseconds);
                return new RedactionResult(text, false, FrozenDictionary<string, int>.Empty, []);
            }
        }

        return new RedactionResult(
            current,
            wasRedacted,
            counts.Count > 0 ? counts.ToFrozenDictionary(StringComparer.Ordinal) : FrozenDictionary<string, int>.Empty,
            matched);
    }

    private static Regex Compile(string regex)
    {
        return new Regex(
            regex,
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(McpGatewayConventions.RegexTimeoutMilliseconds));
    }
}
