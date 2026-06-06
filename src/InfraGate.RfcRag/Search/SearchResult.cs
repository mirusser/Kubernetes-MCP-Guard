namespace InfraGate.RfcRag.Search;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "MA0104:Name should not conflict with BCL types",
    Justification = "SearchResult is the requested public RFC RAG contract name.")]
public sealed record class SearchResult(
    Guid Id,
    int RfcNumber,
    string Title,
    string Section,
    string? Heading,
    string Excerpt,
    string SourcePath,
    string Url,
    double Score);
