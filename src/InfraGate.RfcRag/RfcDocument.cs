using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag;

/// <summary>
/// Represents a fully parsed RFC document with metadata, sections, ABNF blocks,
/// and normative keyword occurrences.
/// </summary>
public sealed record class RfcDocument
{
    /// <summary>Metadata extracted from the RFC front matter.</summary>
    public RfcMetadata Metadata { get; init; } = new();

    /// <summary>Section-level chunks of the RFC body text.</summary>
    public IReadOnlyList<RfcSection> Sections { get; init; } = [];

    /// <summary>ABNF grammar blocks found in the RFC.</summary>
    public IReadOnlyList<RfcAbnfBlock> AbnfBlocks { get; init; } = [];

    /// <summary>Normative keyword occurrences (MUST, SHOULD, etc.).</summary>
    public IReadOnlyList<NormativeOccurrence> NormativeOccurrences { get; init; } = [];

    /// <summary>Raw text content after page header/footer stripping.</summary>
    public string RawText { get; init; } = string.Empty;
}
