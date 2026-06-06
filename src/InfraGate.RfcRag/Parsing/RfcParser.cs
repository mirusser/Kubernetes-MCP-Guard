using System.Text.RegularExpressions;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag.Parsing;

public sealed partial class RfcParser
{
    private static readonly string[] NormativeKeywords =
    [
        "MUST NOT", "MUST", "REQUIRED", "SHALL NOT", "SHALL",
        "SHOULD NOT", "SHOULD", "NOT RECOMMENDED", "RECOMMENDED",
        "MAY", "OPTIONAL"
    ];

    public async Task<RfcDocument> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string fileName = Path.GetFileName(filePath);
        string rawText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

        string title = ExtractTitle(rawText, fileName);
        string cleanedText = StripPageHeadersFooters(rawText);
        int rfcNumber = ExtractRfcNumber(fileName);

        var metadata = new RfcMetadata
        {
            Number = rfcNumber,
            Title = title,
            Date = ExtractField(cleanedText, "Date:"),
            Category = ExtractCategory(cleanedText),
            Obsoletes = ExtractIntArray(cleanedText, "Obsoletes:"),
            Updates = ExtractIntArray(cleanedText, "Updates:"),
            Authors = ExtractAuthors(cleanedText),
            Issn = ExtractField(cleanedText, "ISSN:")
        };

        string bodyText = StripFrontMatter(cleanedText);
        bodyText = StripTableOfContents(bodyText);
        string url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}";

        var sections = SplitIntoSections(bodyText, rfcNumber, title, fileName, url);
        var abnfBlocks = ExtractAbnfBlocks(bodyText, sections, rfcNumber);
        var normativeOccurrences = ExtractNormativeOccurrences(bodyText, sections, rfcNumber);

        return new RfcDocument
        {
            Metadata = metadata,
            Sections = sections,
            AbnfBlocks = abnfBlocks,
            NormativeOccurrences = normativeOccurrences
        };
    }

    private static int ExtractRfcNumber(string fileName)
    {
        var match = RfcNumberRegex().Match(fileName);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static string ExtractTitle(string rawText, string fileName)
    {
        var match = TitleRegex().Match(rawText);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // Fallback: use first non-empty line
        using var reader = new StringReader(rawText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            if (line.Length > 0)
                return line;
        }

        return fileName;
    }

    private static string ExtractField(string text, string fieldName)
    {
        var match = FieldRegex().Match(text, 0);
        while (match.Success)
        {
            if (match.Groups[1].Value.Equals(fieldName, StringComparison.Ordinal))
                return match.Groups[2].Value.Trim();
            match = match.NextMatch();
        }

        return string.Empty;
    }

    private static string ExtractCategory(string text)
    {
        var match = CategoryRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static int[] ExtractIntArray(string text, string fieldName)
    {
        var match = FieldRegex().Match(text, 0);
        while (match.Success)
        {
            if (match.Groups[1].Value.Equals(fieldName, StringComparison.Ordinal))
            {
                string value = match.Groups[2].Value.Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? []
                    : value.Split(',').Select(s =>
                    {
                        var m = RfcRefRegex().Match(s.Trim());
                        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
                    }).Where(n => n > 0).ToArray();
            }
            match = match.NextMatch();
        }

        return [];
    }

    private static string[] ExtractAuthors(string text)
    {
        var match = AuthorsRegex().Match(text);
        return match.Success
            ? match.Groups[1].Value.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToArray()
            : [];
    }

    private static string StripPageHeadersFooters(string text)
    {
        // Remove RFC page headers like "RFC 9110" at the top of pages
        text = PageHeaderRegex().Replace(text, "");
        // Remove page numbers and footer artifacts
        text = PageFooterRegex().Replace(text, "");
        return text;
    }

    private static string StripFrontMatter(string text)
    {
        // Remove everything up to and including the "Status of This Memo" or first major section
        int statusIndex = text.IndexOf("Status of This Memo", StringComparison.Ordinal);
        if (statusIndex >= 0)
        {
            int nextSection = text.IndexOf("\n\r\n", statusIndex, StringComparison.Ordinal);
            if (nextSection < 0)
                nextSection = text.IndexOf("\n\n\n", statusIndex, StringComparison.Ordinal);
            if (nextSection > 0)
                return text[(nextSection + 3)..];
        }

        return text;
    }

    private static string StripTableOfContents(string text)
    {
        // Remove the table of contents section
        var match = TocRegex().Match(text);
        if (match.Success)
        {
            text = text[..match.Index] + text[(match.Index + match.Length)..];
        }

        return text;
    }

    private static string StripPageArtifacts(string line)
    {
        // Remove trailing page numbers like "[Page 42]"
        line = PageArtifactRegex().Replace(line, "");
        return line.TrimEnd();
    }

    private static IReadOnlyList<RfcSection> SplitIntoSections(
        string bodyText,
        int rfcNumber,
        string title,
        string fileName,
        string url)
    {
        var sections = new List<RfcSection>();
        using var reader = new StringReader(bodyText);

        string currentHeading = string.Empty;
        var currentLines = new List<string>();
        string? currentSection = null;
        string? line;
        int sectionCounter = 0;

        void FlushSection()
        {
            if (currentSection is null || currentLines.Count == 0)
                return;

            string text = string.Join("\n", currentLines).Trim();
            if (text.Length == 0)
                return;

            sections.Add(new RfcSection
            {
                Id = Guid.NewGuid(),
                RfcNumber = rfcNumber,
                Title = title,
                Section = currentSection,
                Heading = currentHeading.Length > 0 ? currentHeading : null,
                Text = text,
                SourcePath = fileName,
                Url = url
            });
        }

        while ((line = reader.ReadLine()) is not null)
        {
            string stripped = StripPageArtifacts(line);

            if (string.IsNullOrWhiteSpace(stripped))
            {
                currentLines.Add(string.Empty);
                continue;
            }

            // Check for section heading like "1.", "1.1.", "Appendix A.", etc.
            var sectionMatch = SectionHeadingRegex().Match(stripped);
            if (sectionMatch.Success && stripped.TrimEnd().EndsWith(sectionMatch.Groups[0].Value.TrimEnd(), StringComparison.Ordinal))
            {
                FlushSection();
                currentSection = sectionMatch.Groups[1].Value;
                currentHeading = sectionMatch.Groups[2].Value.Trim();
                currentLines.Clear();
                sectionCounter++;

                // Skip the heading line itself (don't add to section text)
                continue;
            }

            currentLines.Add(stripped);
        }

        FlushSection();

        return sections.AsReadOnly();
    }

    private static IReadOnlyList<RfcAbnfBlock> ExtractAbnfBlocks(
        string bodyText,
        IReadOnlyList<RfcSection> sections,
        int rfcNumber)
    {
        var blocks = new List<RfcAbnfBlock>();
        var abnfRegex = AbnfBlockRegex();

        foreach (var section in sections)
        {
            var matches = abnfRegex.Matches(section.Text);
            foreach (Match match in matches)
            {
                string abnfText = match.Groups[1].Value.Trim();
                var ruleNames = ExtractRuleNames(abnfText);

                blocks.Add(new RfcAbnfBlock
                {
                    Id = Guid.NewGuid(),
                    SectionId = section.Id,
                    RfcNumber = rfcNumber,
                    Section = section.Section,
                    AbnfText = abnfText,
                    RuleNames = ruleNames
                });
            }
        }

        return blocks.AsReadOnly();
    }

    private static string[] ExtractRuleNames(string abnfText)
    {
        var names = new List<string>();
        using var reader = new StringReader(abnfText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            // ABNF rule definition: "rulename = ..." or "rulename =/ ..."
            var match = AbnfRuleRegex().Match(line);
            if (match.Success)
            {
                names.Add(match.Groups[1].Value.Trim());
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<NormativeOccurrence> ExtractNormativeOccurrences(
        string bodyText,
        IReadOnlyList<RfcSection> sections,
        int rfcNumber)
    {
        var occurrences = new List<NormativeOccurrence>();

        foreach (var section in sections)
        {
            int lineOffset = 0;
            using var reader = new StringReader(section.Text);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                string upperLine = line.ToUpperInvariant();
                foreach (string keyword in NormativeKeywords)
                {
                    int index = 0;
                    while ((index = upperLine.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
                    {
                        // Verify it's a standalone keyword (not part of a longer word)
                        if ((index == 0 || !char.IsLetterOrDigit(upperLine[index - 1])) &&
                            (index + keyword.Length >= upperLine.Length || !char.IsLetterOrDigit(upperLine[index + keyword.Length])))
                        {
                            occurrences.Add(new NormativeOccurrence
                            {
                                Id = Guid.NewGuid(),
                                SectionId = section.Id,
                                RfcNumber = rfcNumber,
                                Keyword = keyword,
                                LineOffset = lineOffset
                            });
                        }

                        index += keyword.Length;
                    }
                }

                lineOffset++;
            }
        }

        return occurrences.AsReadOnly();
    }

    /// <summary>
    /// Removes the Table of Contents block from a list of text lines.
    /// The TOC block starts with an indented "Table of Contents" line and
    /// continues through subsequent indented lines until a non-indented,
    /// non-empty line is found (real content heading).
    /// </summary>
    public static IReadOnlyList<string> RemoveTocBlock(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        bool inToc = false;

        foreach (string line in lines)
        {
            if (!inToc && line.TrimStart().StartsWith("Table of Contents", StringComparison.OrdinalIgnoreCase))
            {
                inToc = true;
                continue;
            }

            if (inToc)
            {
                if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                    continue;

                inToc = false;
            }

            result.Add(line);
        }

        return result;
    }

    [GeneratedRegex(@"rfc(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RfcNumberRegex();

    [GeneratedRegex(@"^(.+?)(?:\r?\n|$)", RegexOptions.Multiline, 1000)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"([A-Za-z\s/]+):\s*(.*?)(?=\n[A-Za-z\s/]+:\s|\n{2,}|\Z)", RegexOptions.Singleline, 1000)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"Category:\s*(.+?)(?:\r?\n|$)", RegexOptions.Multiline, 1000)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"RFC\s*(\d+)", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex RfcRefRegex();

    [GeneratedRegex(@"Authors?:\s*(.+?)(?:\r?\n\s)", RegexOptions.Singleline, 1000)]
    private static partial Regex AuthorsRegex();

    [GeneratedRegex(@"(?m)^RFC\s+\d+\s+.*$", RegexOptions.None, 1000)]
    private static partial Regex PageHeaderRegex();

    [GeneratedRegex(@"(?m)^\[Page\s+\d+\]$", RegexOptions.None, 1000)]
    private static partial Regex PageFooterRegex();

    [GeneratedRegex(@"(?m)^\[Page\s+\d+\]", RegexOptions.None, 1000)]
    private static partial Regex PageArtifactRegex();

    [GeneratedRegex(@"Table of Contents\s*$.*?(?=^\d+\.\s)", RegexOptions.Singleline | RegexOptions.Multiline, 1000)]
    private static partial Regex TocRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)*|Appendix\s+[A-Z](?:\.\d+)*)\s+(.+?)$", RegexOptions.Multiline, 1000)]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"```abnf\s*\n(.*?)\n```", RegexOptions.Singleline, 1000)]
    private static partial Regex AbnfBlockRegex();

    [GeneratedRegex(@"^\s*([a-zA-Z][a-zA-Z0-9-]*)\s*=\s*/?\s*", RegexOptions.Multiline, 1000)]
    private static partial Regex AbnfRuleRegex();
}
