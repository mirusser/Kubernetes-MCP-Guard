using System.Text.RegularExpressions;

namespace InfraGate.RfcRag.Parsing;

public sealed partial class RfcParser
{
    public async Task<RfcDocument> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string rawText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return ParseContent(rawText, Path.GetFileName(filePath));
    }

    /// <summary>
    /// Parses an RFC document from already-read text, avoiding a second file read when the
    /// caller already holds the file bytes (e.g. for SHA-256 hashing).
    /// </summary>
    public RfcDocument ParseContent(string rawText, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // PostgreSQL TEXT columns reject embedded null bytes (\0).
        // Some RFC files in the mirror contain them (e.g. old format encoding artifacts).
        // Guard with Contains to avoid the allocation in the common null-free case.
        if (rawText.Contains('\0', StringComparison.Ordinal))
        {
            rawText = rawText.Replace("\0", string.Empty, StringComparison.Ordinal);
        }

        string title = ExtractTitle(rawText, fileName);
        int rfcNumber = ExtractRfcNumber(fileName);
        if (rfcNumber == 0)
        {
            throw new FormatException($"Could not extract RFC number from filename '{fileName}'.");
        }

        // Final safety net: if ExtractTitle somehow returned empty/whitespace
        // (shouldn't happen after the fallback change in ExtractTitle), use
        // a placeholder so the downstream DB upsert doesn't crash indexing.
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"RFC {rfcNumber}";
        }

        string cleanedText = StripPageHeadersFooters(rawText);

        var metadata = new RfcMetadata
        {
            Number = rfcNumber,
            Title = title,
            Date = ExtractField(cleanedText, "Date"),
            Category = ExtractCategory(cleanedText),
            Obsoletes = ExtractIntArray(cleanedText, "Obsoletes"),
            Updates = ExtractIntArray(cleanedText, "Updates"),
            Authors = ExtractAuthors(cleanedText),
            Issn = ExtractField(cleanedText, "ISSN")
        };

        string bodyText = StripFrontMatter(cleanedText);
        bodyText = StripTableOfContents(bodyText);
        string url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}";

        var sections = SplitIntoSections(bodyText, rfcNumber, title, fileName, url);
        var abnfBlocks = ExtractAbnfBlocks(bodyText, sections, rfcNumber);
        var normativeOccurrences = ExtractNormativeOccurrences(bodyText, sections, rfcNumber);

        metadata = metadata with { GrammarStyle = DetectGrammarStyle(sections) };

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
        // RFC titles are indented (5+ leading spaces) on their own line(s) between
        // the header metadata block and "Abstract". Walk backwards from Abstract to
        // find the indented title, handling multi-line titles.
        int abstractPos = rawText.IndexOf("Abstract", StringComparison.Ordinal);
        if (abstractPos > 0)
        {
            string beforeAbstract = rawText[..abstractPos];
            var lines = beforeAbstract.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var titleLines = new List<string>();
            bool foundIndented = false;

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string trimmed = lines[i].TrimEnd();
                if (trimmed.Length == 0)
                {
                    if (foundIndented) break; // blank line after title ends the title block
                    continue; // skip leading blank lines
                }

                bool startsWithFiveSpaces = lines[i].Length > 5 && lines[i].AsSpan(0, 5).IndexOfAnyExcept(' ') < 0;
                if (startsWithFiveSpaces && !trimmed.StartsWith("Abstract", StringComparison.Ordinal))
                {
                    foundIndented = true;
                    titleLines.Insert(0, trimmed);
                }
                else if (foundIndented)
                {
                    break; // non-indented line after title ends the title block
                }
            }

            if (titleLines.Count > 0)
                return string.Join(" ", titleLines).Trim();
        }

        // Fallback: walk backwards from "Status of This Memo" to find indented title.
        // Needed for RFCs without an Abstract heading (e.g. RFC 3986).
        int statusPos = rawText.IndexOf("Status of This Memo", StringComparison.OrdinalIgnoreCase);
        if (statusPos > 0)
        {
            string beforeStatus = rawText[..statusPos];
            var lines = beforeStatus.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var titleLines = new List<string>();
            bool foundIndented = false;

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string trimmed = lines[i].TrimEnd();
                if (trimmed.Length == 0)
                {
                    if (foundIndented) break;
                    continue;
                }

                bool startsWithFiveSpaces = lines[i].Length > 5 && lines[i].AsSpan(0, 5).IndexOfAnyExcept(' ') < 0;
                if (startsWithFiveSpaces && !trimmed.StartsWith("Status of This Memo", StringComparison.OrdinalIgnoreCase))
                {
                    foundIndented = true;
                    titleLines.Insert(0, trimmed);
                }
                else if (foundIndented)
                {
                    break;
                }
            }

            if (titleLines.Count > 0)
                return string.Join(" ", titleLines).Trim();
        }

        // Final fallback: extract RFC number from the filename so the title is
        // never empty or whitespace. Some RFC files have unusual formatting that
        // the indented-title heuristics cannot parse.
        var rfcMatch = RfcNumberRegex().Match(fileName);
        return rfcMatch.Success ? $"RFC {rfcMatch.Groups[1].Value}" : fileName;
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
        // Find "Status of This Memo" and strip everything up to and including
        // the indented status paragraph that follows it.
        int statusIndex = text.IndexOf("Status of This Memo", StringComparison.OrdinalIgnoreCase);
        if (statusIndex < 0)
            return text;

        // Walk forward from Status heading, skipping blank lines and the indented
        // status paragraph. Stop at the first non-indented, non-blank line
        // (typically "Abstract", "Copyright Notice", or a section heading like "1.  ").
        int pos = statusIndex + "Status of This Memo".Length;

        while (pos < text.Length)
        {
            int nextNewline = text.IndexOf('\n', pos);
            if (nextNewline < 0)
                return text;

            int lineLen = nextNewline - pos;
            if (lineLen == 0)
            {
                // Empty line — skip
                pos = nextNewline + 1;
                continue;
            }

            ReadOnlySpan<char> span = text.AsSpan(pos, lineLen);
            bool allWhitespace = true;
            foreach (char c in span)
            {
                if (!char.IsWhiteSpace(c))
                {
                    allWhitespace = false;
                    break;
                }
            }

            if (allWhitespace || char.IsWhiteSpace(span[0]))
            {
                // Whitespace-only or indented line — skip (still in status paragraph)
                pos = nextNewline + 1;
                continue;
            }

            // First non-indented, non-blank line — start of real content
            return text[pos..];
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
            if (sectionMatch.Success)
            {
                FlushSection();
                currentSection = sectionMatch.Groups[1].Value;
                currentHeading = sectionMatch.Groups[2].Value.Trim();
                currentLines.Clear();

                // Include the heading text as the first line of section body.
                // For old-style RFCs the body starts on the same line as the heading ("1. MUST ...").
                // For new-style RFCs the heading is a short label and the body follows separately.
                currentLines.Add(currentHeading);
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
        var ruleRegex = AbnfRuleRegex();

        foreach (var section in sections)
        {
            string[] lines = section.Text.Split('\n');

            // Count rule definition lines in this section.
            // Only sections with at least 1 rule line are grammar sections;
            // this captures single-rule subsections such as "request-line = method SP ...".
            int ruleCount = 0;
            foreach (string line in lines)
            {
                if (ruleRegex.IsMatch(line))
                    ruleCount++;
            }

            if (ruleCount < 1)
                continue;

            // Group contiguous rule definition + continuation lines into blocks.
            int i = 0;
            while (i < lines.Length)
            {
                // Skip non-rule lines
                while (i < lines.Length && !ruleRegex.IsMatch(lines[i]))
                    i++;

                if (i >= lines.Length)
                    break;

                int blockStart = i;
                i++;

                // Include continuation lines: non-blank, indented, not a new rule
                while (i < lines.Length)
                {
                    string trimmed = lines[i].Trim();
                    if (trimmed.Length == 0)
                        break;
                    if (ruleRegex.IsMatch(lines[i]))
                        break;
                    if (!char.IsWhiteSpace(lines[i][0]))
                        break;
                    i++;
                }

                string blockText = string.Join("\n", lines, blockStart, i - blockStart).Trim();
                if (blockText.Length > 0)
                {
                    blocks.Add(new RfcAbnfBlock
                    {
                        Id = Guid.NewGuid(),
                        SectionId = section.Id,
                        RfcNumber = rfcNumber,
                        Section = section.Section,
                        AbnfText = blockText,
                        RuleNames = ExtractRuleNames(blockText)
                    });
                }
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
                var matches = NormativeKeywordsRegex().Matches(line);
                foreach (Match match in matches)
                {
                    occurrences.Add(new NormativeOccurrence
                    {
                        Id = Guid.NewGuid(),
                        SectionId = section.Id,
                        RfcNumber = rfcNumber,
                        Keyword = match.Groups[1].Value.ToUpperInvariant(),
                        LineOffset = lineOffset
                    });
                }

                lineOffset++;
            }
        }

        return occurrences.AsReadOnly();
    }

    private static string DetectGrammarStyle(IReadOnlyList<RfcSection> sections)
    {
        int abnfLines = 0;
        int tlsLines = 0;
        int cddlLines = 0;
        int asn1Lines = 0;

        var abnfRuleRegex = AbnfRuleRegex();
        var tlsStructRegex = TlsStructRegex();
        var tlsEnumRegex = TlsEnumRegex();
        var tlsSelectRegex = TlsSelectRegex();
        var cddlGroupRegex = CddlGroupRegex();
        var cddlTypeRuleRegex = CddlTypeRuleRegex();
        var asn1Regex = Asn1Regex();

        foreach (RfcSection section in sections)
        {
            string[] lines = section.Text.Split('\n');
            foreach (string line in lines)
            {
                if (cddlGroupRegex.IsMatch(line) || cddlTypeRuleRegex.IsMatch(line))
                {
                    cddlLines++;
                    continue;
                }
                if (abnfRuleRegex.IsMatch(line))
                    abnfLines++;
                if (tlsStructRegex.IsMatch(line) || tlsEnumRegex.IsMatch(line) || tlsSelectRegex.IsMatch(line))
                    tlsLines++;
                if (asn1Regex.IsMatch(line))
                    asn1Lines++;
            }
        }

        int totalGrammarLines = abnfLines + tlsLines + cddlLines + asn1Lines;
        if (totalGrammarLines == 0)
            return GrammarStyleConstants.None;

        if (abnfLines > totalGrammarLines / 2)
            return GrammarStyleConstants.Abnf;
        if (tlsLines > totalGrammarLines / 2)
            return GrammarStyleConstants.TlsPresentationLang;
        if (cddlLines > totalGrammarLines / 2)
            return GrammarStyleConstants.Cddl;
        if (asn1Lines > totalGrammarLines / 2)
            return GrammarStyleConstants.Asn1;

        if (abnfLines >= tlsLines && abnfLines >= cddlLines && abnfLines >= asn1Lines)
            return GrammarStyleConstants.Abnf;
        if (tlsLines >= cddlLines && tlsLines >= asn1Lines)
            return GrammarStyleConstants.TlsPresentationLang;
        if (cddlLines >= asn1Lines)
            return GrammarStyleConstants.Cddl;

        return GrammarStyleConstants.Asn1;
    }

    // Longest-first alternation ensures "MUST NOT" is matched before "MUST" at the same position,
    // preventing "MUST NOT" from being counted as both "MUST NOT" and "MUST".
    [GeneratedRegex(@"\b(MUST NOT|MUST|REQUIRED|SHALL NOT|SHALL|SHOULD NOT|SHOULD|NOT RECOMMENDED|RECOMMENDED|MAY|OPTIONAL)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex NormativeKeywordsRegex();

    [GeneratedRegex(@"rfc(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RfcNumberRegex();

    [GeneratedRegex(@"([A-Za-z\s/]+):\s*(.*?)(?=\n[A-Za-z\s/]+:\s|\n{2,}|\Z)", RegexOptions.Singleline, 1000)]
    private static partial Regex FieldRegex();

    [GeneratedRegex(@"Category:\s*(.+?)(?:\r?\n|$)", RegexOptions.Multiline, 1000)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"RFC\s*(\d+)", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex RfcRefRegex();

    [GeneratedRegex(@"Authors?:\s*(.+?)(?:\r?\n\s)", RegexOptions.Singleline, 1000)]
    private static partial Regex AuthorsRegex();

    [GeneratedRegex(@"(?m)^[^\S\r\n]*RFC\s+\d+\s+.*$", RegexOptions.None, 1000)]
    private static partial Regex PageHeaderRegex();

    [GeneratedRegex(@"(?m)^[^\S\r\n]*\[Page\s+\d+\]$", RegexOptions.None, 1000)]
    private static partial Regex PageFooterRegex();

    [GeneratedRegex(@"(?m)^[^\S\r\n]*\[Page\s+\d+\]", RegexOptions.None, 1000)]
    private static partial Regex PageArtifactRegex();

    [GeneratedRegex(@"Table of Contents\s*$.*?(?=^\d+\.\s)", RegexOptions.Singleline | RegexOptions.Multiline, 1000)]
    private static partial Regex TocRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)*|Appendix\s+[A-Z](?:\.\d+)*)\.\s+(.+?)$", RegexOptions.Multiline, 1000)]
    private static partial Regex SectionHeadingRegex();

    [GeneratedRegex(@"^\w[\w-]*\s+(::=)|\s*DEFINITIONS\s+::", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Asn1Regex();

    [GeneratedRegex(@"^\s*\w[\w-]*\s*=\s*[\{\(]", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CddlGroupRegex();

    [GeneratedRegex(@"^\s*\w[\w-]*\s*=\s*\w[\w-]*\s*(?:/|\()", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CddlTypeRuleRegex();

    [GeneratedRegex(@"^\s*struct\s*\{", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TlsStructRegex();

    [GeneratedRegex(@"^\s*enum\s*\{", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TlsEnumRegex();

    [GeneratedRegex(@"^\s*select\s*\(", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TlsSelectRegex();

    [GeneratedRegex(@"^\s*([a-zA-Z][a-zA-Z0-9-]*)\s*=\s*/?\s*", RegexOptions.Multiline, 1000)]
    private static partial Regex AbnfRuleRegex();
}
