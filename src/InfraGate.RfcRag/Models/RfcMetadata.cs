namespace InfraGate.RfcRag.Models;

/// <summary>
/// Extracted metadata from the RFC front matter (first-page header block).
/// </summary>
public sealed record class RfcMetadata
{
    /// <summary>RFC number (e.g., 9110).</summary>
    public int Number { get; init; }

    /// <summary>RFC title (e.g., "HTTP Semantics").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Publication month and year (e.g., "June 2022").</summary>
    public string? Date { get; init; }

    /// <summary>Category: Standards Track, Informational, Experimental, Best Current Practice, Historic.</summary>
    public string? Category { get; init; }

    /// <summary>RFCs obsoleted by this RFC (may be multiple, comma-separated in source).</summary>
    public int[] Obsoletes { get; init; } = [];

    /// <summary>RFCs updated by this RFC.</summary>
    public int[] Updates { get; init; } = [];

    /// <summary>Authors from the header block.</summary>
    public string[] Authors { get; init; } = [];

    /// <summary>ISSN (always "2070-1721" for modern RFCs).</summary>
    public string? Issn { get; init; }

    /// <summary>Grammar style detected during parsing: "abnf", "tls-presentation-lang", "cddl", "asn.1", or "none".</summary>
    public string GrammarStyle { get; init; } = GrammarStyleConstants.None;
}
