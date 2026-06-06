using InfraGate.RfcRag;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Tests.UnitTests;

public sealed class RfcParserTests
{
    private static readonly RfcParser Parser = new();

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsCorrectMetadata()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotNull(document);
        Assert.NotNull(document.Metadata);
        Assert.Equal(2119, document.Metadata.Number);
        Assert.Contains("Key words", document.Metadata.Title);
        Assert.NotEmpty(document.Sections);
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotEmpty(document.Sections);
        RfcSection? introSection = document.Sections.FirstOrDefault(s => s.Section == "1");
        Assert.NotNull(introSection);
        Assert.Contains("MUST", introSection!.Text);
        Assert.Contains("rfc2119.txt", introSection.SourcePath);
        Assert.Contains("rfc-editor.org", introSection.Url);
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsNormativeKeywords()
    {
        string fixturePath = Path.Combine("TestData", "rfc2119.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        var mustOccurrences = document.NormativeOccurrences
            .Where(n => n.Keyword == "MUST")
            .ToList();
        Assert.NotEmpty(mustOccurrences);

        var shouldOccurrences = document.NormativeOccurrences
            .Where(n => n.Keyword == "SHOULD")
            .ToList();
        Assert.NotEmpty(shouldOccurrences);
    }

    [Fact]
    public async Task ParseAsync_RealRfc9110_ExtractsComplexSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(9110, document.Metadata.Number);
        Assert.Contains("HTTP Semantics", document.Metadata.Title);
        Assert.True(document.Sections.Count > 5, $"Expected >5 sections, got {document.Sections.Count}");

        RfcSection? section63 = document.Sections.FirstOrDefault(s => s.Section == "6.3");
        Assert.NotNull(section63);
    }

    [Fact]
    public async Task ParseAsync_RealRfc8446_ExtractsMultipleSections()
    {
        string fixturePath = Path.Combine("TestData", "rfc8446.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.Equal(8446, document.Metadata.Number);
        Assert.Contains("TLS", document.Metadata.Title);
        Assert.True(document.Sections.Count > 3, $"Expected >3 sections, got {document.Sections.Count}");
    }

    [Fact]
    public async Task ParseAsync_RealRfc2119_ExtractsAbnfBlocks()
    {
        string fixturePath = Path.Combine("TestData", "rfc9110.txt");
        RfcDocument document = await Parser.ParseAsync(fixturePath, CancellationToken.None);

        Assert.NotEmpty(document.AbnfBlocks);
    }

    [Fact]
    public async Task ParseAsync_InvalidFilename_ThrowsFormatException()
    {
        string fixturePath = Path.Combine("TestData", "badfile.txt");
        await Assert.ThrowsAsync<FormatException>(() => Parser.ParseAsync(fixturePath, CancellationToken.None));
    }
}
