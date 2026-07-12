using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfraGate.McpGateway.Tests.UnitTests;

public sealed class SensitiveDataRedactorTests
{
    public static TheoryData<string, string, string> PatternCases = new()
    {
        { "private-key", "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA0Z3S8...", "RSA PRIVATE KEY" },
        { "jwt", "Authorization: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c" },
        { "aws-key", "access key AKIAIOSFODNN7EXAMPLE is exposed", "AKIAIOSFODNN7EXAMPLE" },
        { "bearer-token", "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.token", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.token" },
        { "basic-auth", "Authorization: Basic dXNlcjpwYXNzd29yZHRlc3QxMjM0NTY3OA==", "dXNlcjpwYXNzd29yZHRlc3QxMjM0NTY3OA==" },
        { "connection-string", "Server=myServer;Database=myDB;Password=superSecret123!", "superSecret123!" },
        { "password-param", "connection password=foo123 string", "foo123" },
        { "secret-param", "client_secret=abc123def456", "abc123def456" },
        { "token-param", "auth_token=xyz789", "xyz789" },
        { "api-key-param", "x-api-key=deadbeefcafebabe", "deadbeefcafebabe" }
    };

    [Theory]
    [MemberData(nameof(PatternCases))]
    public void Redact_DefaultPattern_RedactsAndWasRedactedTrue(
        string patternName,
        string input,
        string secretValue)
    {
        var redactor = CreateDefaultRedactor();

        RedactionResult result = redactor.Redact(input);

        Assert.Contains(McpGatewayConventions.SensitiveDataRedaction.Placeholder(patternName), result.Text);
        Assert.DoesNotContain(secretValue, result.Text);
        Assert.True(result.WasRedacted);
        Assert.Contains(patternName, result.PatternsMatched);
        Assert.True(result.CountByPattern.ContainsKey(patternName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cluster status is healthy")]
    public void Redact_CleanInput_ReturnsOriginalTextAndWasRedactedFalse(string input)
    {
        var redactor = CreateDefaultRedactor();

        RedactionResult result = redactor.Redact(input);

        Assert.Equal(input, result.Text);
        Assert.False(result.WasRedacted);
        Assert.Empty(result.PatternsMatched);
        Assert.Empty(result.CountByPattern);
    }

    [Fact]
    public void Redact_NullInput_ThrowsArgumentNullException()
    {
        var redactor = CreateDefaultRedactor();

        Assert.Throws<ArgumentNullException>(() => redactor.Redact(null!));
    }

    [Fact]
    public void Redact_MultipleMatchesSamePattern_IncrementsCountByPattern()
    {
        var redactor = CreateDefaultRedactor();

        RedactionResult result = redactor.Redact("AKIAIOSFODNN7EXAMPLE and AKIAIOSFODNN7EXAMPLE again");

        Assert.Contains("[redacted: aws-key]", result.Text);
        Assert.Equal(2, result.CountByPattern["aws-key"]);
    }

    [Fact]
    public void Redact_MultipleDifferentPatterns_ListsAllPatternsMatched()
    {
        var redactor = CreateDefaultRedactor();

        RedactionResult result = redactor.Redact("password=foo secret=bar token=baz");

        Assert.Contains("[redacted: password-param]", result.Text);
        Assert.Contains("[redacted: secret-param]", result.Text);
        Assert.Contains("[redacted: token-param]", result.Text);
        Assert.Equal(3, result.PatternsMatched.Count);
        Assert.Contains("password-param", result.PatternsMatched);
        Assert.Contains("secret-param", result.PatternsMatched);
        Assert.Contains("token-param", result.PatternsMatched);
    }

    [Fact]
    public void Redact_RegexTimeout_ReturnsOriginalText()
    {
        var pattern = new RedactionPattern("timeout", @"(a+)+$");
        var logger = new CapturingLogger<SensitiveDataRedactor>();
        var redactor = new SensitiveDataRedactor([pattern], logger);
        string input = new string('a', 100000) + "b";

        RedactionResult result = redactor.Redact(input);

        Assert.Equal(input, result.Text);
        Assert.False(result.WasRedacted);
        Assert.Contains(logger.Messages, message => message.Contains("timed out", StringComparison.Ordinal));
    }

    [Fact]
    public void Redact_MatchedSecrets_DoNotAppearInResultOrLogs()
    {
        var logger = new CapturingLogger<SensitiveDataRedactor>();
        var redactor = new SensitiveDataRedactor(McpGatewayConventions.SensitiveDataRedaction.Defaults, logger);
        const string secret = "AKIAIOSFODNN7EXAMPLE";

        RedactionResult result = redactor.Redact($"key={secret}");

        Assert.DoesNotContain(secret, result.Text);
        Assert.All(result.CountByPattern, pair => Assert.IsType<int>(pair.Value));
        Assert.All(logger.Messages, message => Assert.DoesNotContain(secret, message));
    }

    private static SensitiveDataRedactor CreateDefaultRedactor() =>
        new(McpGatewayConventions.SensitiveDataRedaction.Defaults, NullLogger<SensitiveDataRedactor>.Instance);
}
