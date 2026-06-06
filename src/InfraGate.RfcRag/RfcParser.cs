using System.Text.RegularExpressions;
using InfraGate.RfcRag.Models;

namespace InfraGate.RfcRag;

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
        string[] lines = rawText.Split('\n');
        int rfcNumber = ExtractRfcNumber(fileName);
        RfcMetadata metadata = ExtractMetadata(lines, rfcNumber);
        IReadOnlyList<string> bodyLines = StripPageArtifacts(lines);
        bodyLines = RemoveTocBlock(bodyLines);
        string url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}.txt";
        IReadOnlyList<RfcSection> sections = SplitIntoSections(bodyLines, rfcNumber, metadata.Title, fileName, url);
        IReadOnlyList<RfcAbnfBlock> abnfBlocks = ExtractAbnfBlocks(sections);
        IReadOnlyList<NormativeOccurrence> normative = ExtractNormativeKeywords(sections);
        return new RfcDocument { Metadata = metadata, Sections = sections, AbnfBlocks = abnfBlocks, NormativeOccurrences = normative, RawText = string.Join('\n', bodyLines) };
    }

    private static int ExtractRfcNumber(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        if (name.StartsWith("rfc", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[3..], out int n)) return n;
        throw new FormatException($"Cannot extract RFC number from filename '{fileName}'.");
    }

    private static RfcMetadata ExtractMetadata(string[] lines, int rfcNumber)
    {
        string? title = null, date = null, category = null;
        var obsoletes = new List<int>();
        var updates = new List<int>();
        bool inHeader = true, inObsContinuation = false, inUpdContinuation = false;
        int consecutiveBlanks = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveBlanks++;
                inObsContinuation = false; inUpdContinuation = false;
                if (inHeader && title is not null && consecutiveBlanks >= 2) inHeader = false;
                continue;
            }

            consecutiveBlanks = 0;
            if (!inHeader) break;

            // Handle continuation lines for multi-line Obsoletes/Updates
            if (inObsContinuation)
            {
                ParseNumbersFromLine(line, obsoletes);
                if (!IsContinuationLine(line)) inObsContinuation = false;
                continue;
            }
            if (inUpdContinuation)
            {
                ParseNumbersFromLine(line, updates);
                if (!IsContinuationLine(line)) inUpdContinuation = false;
                continue;
            }

            var obsMatch = ObsoletesRegex().Match(line);
            if (obsMatch.Success)
            {
                ParseNumbersFromLine(obsMatch.Groups[1].Value, obsoletes);
                inObsContinuation = IsMultiLineValue(line);
                continue;
            }

            var updMatch = UpdatesRegex().Match(line);
            if (updMatch.Success)
            {
                ParseNumbersFromLine(updMatch.Groups[1].Value, updates);
                inUpdContinuation = IsMultiLineValue(line);
                continue;
            }

            var catMatch = CategoryRegex().Match(line);
            if (catMatch.Success) { category = catMatch.Groups[1].Value.Trim(); continue; }

            var dateMatch = DateRegex().Match(line);
            if (dateMatch.Success) { date = line.Trim(); continue; }

            if (title is null && IsTitleCandidate(line))
                title = line.Trim();
        }

        return new RfcMetadata { Number = rfcNumber, Title = title ?? string.Empty, Date = date, Category = category, Obsoletes = obsoletes.ToArray(), Updates = updates.ToArray() };
    }

    private static bool IsMultiLineValue(string line) => line.EndsWith(',');
    private static bool IsContinuationLine(string line) => line.TrimStart().StartsWith(',');

    private static void ParseNumbersFromLine(string text, List<int> target)
    {
        foreach (string num in text.Split(','))
            if (int.TryParse(num.Trim(), out int n)) target.Add(n);
    }

    private static bool IsTitleCandidate(string line)
    {
        string t = line.Trim();
        if (t.Length < 5) return false;
        string[] skips = ["Network Working Group", "Request for Comments:", "BCP:", "STD:", "Category:", "ISSN:", "Obsoletes:", "Updates:", "Status of This Memo", "Abstract", "Copyright", "Internet Engineering Task Force"];
        foreach (string s in skips)
            if (t.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return false;
        // Skip continuation lines (indented, start with numbers/commas)
        if (char.IsWhiteSpace(line[0]) && t.Any(c => char.IsDigit(c) || c == ',')) return false;
        return t.Any(char.IsUpper);
    }

    private static IReadOnlyList<string> StripPageArtifacts(string[] lines)
    {
        var result = new List<string>(lines.Length);
        bool headerSkipped = false;
        foreach (string line in lines)
        {
            string t = line.TrimEnd('\r');
            if (t.Contains('\f', StringComparison.Ordinal))
            {
                t = t.Replace("\f", "", StringComparison.Ordinal).Trim();
                // Each form feed starts a new page — reset so its header lines
                // (e.g. "RFC XXXX  Title  Month Year") are stripped.
                // Reset BEFORE the empty-line guard so bare \f lines still
                // reset the header state for the following page.
                headerSkipped = false;
                if (string.IsNullOrWhiteSpace(t)) continue;
            }
            if (PageFooterRegex().IsMatch(t)) continue;
            if (!headerSkipped)
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (IsHeaderLine(t)) continue;
                headerSkipped = true;
            }
            result.Add(line.TrimEnd('\r', '\n'));
        }
        return result;
    }

    /// Remove the Table of Contents block from body lines.
    /// TOC starts with "Table of Contents" or "Contents" on its own (trimmed) line.
    /// TOC entries are indented lines with section numbers; the block ends just
    /// before the first non-blank, non-indented line after the TOC marker.
    internal static IReadOnlyList<string> RemoveTocBlock(IReadOnlyList<string> lines)
    {
        int tocStart = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (string.Equals(t, "Table of Contents", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "Contents", StringComparison.OrdinalIgnoreCase))
            {
                tocStart = i;
                break;
            }
        }

        if (tocStart < 0) return lines;

        // Find end of TOC: skip blank and indented lines
        int tocEnd = tocStart + 1;
        while (tocEnd < lines.Count)
        {
            if (string.IsNullOrWhiteSpace(lines[tocEnd]))
            {
                tocEnd++;
                continue;
            }

            if (char.IsWhiteSpace(lines[tocEnd][0]))
            {
                tocEnd++;
                continue;
            }

            break;
        }

        if (tocEnd <= tocStart + 1) return lines;

        return lines.Take(tocStart).Concat(lines.Skip(tocEnd)).ToList();
    }

    private static bool IsHeaderLine(string line)
    {
        string t = line.Trim();
        if (string.IsNullOrWhiteSpace(t)) return true;
        string[] pf = ["Network Working Group", "Request for Comments:", "BCP:", "STD:", "Category:", "ISSN:", "Obsoletes:", "Updates:", "Status of This Memo", "Abstract", "Copyright", "Internet Engineering Task Force"];
        foreach (string p in pf)
            if (t.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        // Skip continuation lines in the header block
        if (char.IsWhiteSpace(line[0])) return true;
        return false;
    }

    private static IReadOnlyList<RfcSection> SplitIntoSections(IReadOnlyList<string> bodyLines, int rfcNumber, string title, string fileName, string url)
    {
        var sections = new List<RfcSection>();
        int? start = null; string? heading = null; string? num = null;
        void Flush(int end)
        {
            if (start is null || num is null) return;
            var sl = bodyLines.Skip(start.Value).Take(end - start.Value).ToList();
            while (sl.Count > 0 && string.IsNullOrWhiteSpace(sl[0])) sl.RemoveAt(0);
            while (sl.Count > 0 && string.IsNullOrWhiteSpace(sl[^1])) sl.RemoveAt(sl.Count - 1);
            if (sl.Count > 0)
                sections.Add(new RfcSection { Id = Guid.NewGuid(), RfcNumber = rfcNumber, Title = title, Section = num, Heading = heading, Text = string.Join('\n', sl), SourcePath = fileName, Url = url });
            start = null; heading = null; num = null;
        }
        for (int i = 0; i < bodyLines.Count; i++)
        {
            var m = SectionHeadingRegex().Match(bodyLines[i]);
            if (m.Success) { Flush(i); num = m.Groups[1].Value.Trim().TrimEnd('.'); heading = m.Groups[2].Value.Trim(); start = i; continue; }
            if (start is null) { num = "0"; heading = "Preamble"; start = i; }
        }
        Flush(bodyLines.Count);
        return sections;
    }

    private static IReadOnlyList<RfcAbnfBlock> ExtractAbnfBlocks(IReadOnlyList<RfcSection> sections)
    {
        var blocks = new List<RfcAbnfBlock>();
        foreach (RfcSection section in sections)
        {
            var ls = section.Text.Split('\n'); int? abnfS = null;
            for (int i = 0; i < ls.Length; i++)
            {
                string line = ls[i].TrimEnd('\r');
                if (AbnfLineRegex().IsMatch(line))
                {
                    abnfS ??= i;
                }
                else if (abnfS is not null)
                {
                    // Inside an ABNF block: accept continuation lines (2+ indent) and blanks.
                    // Stop only when we hit a non-blank, non-indented line.
                    if (string.IsNullOrWhiteSpace(line) || IsAbnfContinuationLine(line))
                        continue;

                    CollectAbnf(abnfS.Value, i, ls, section, blocks);
                    abnfS = null;
                }
            }
            if (abnfS is not null) CollectAbnf(abnfS.Value, ls.Length, ls, section, blocks);
        }
        return blocks;
    }

    private static bool IsAbnfContinuationLine(string line) =>
        line.Length >= 2 && line[0] == ' ' && line[1] == ' ';

    private static void CollectAbnf(int start, int end, string[] ls, RfcSection section, List<RfcAbnfBlock> blocks)
    {
        var bl = new List<string>(); var rn = new List<string>();
        for (int i = start; i < end; i++)
        {
            string l = ls[i].TrimEnd('\r'); bl.Add(l);
            int eq = l.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) { string n = l[..eq].Trim(); if (n.Length > 0 && n.All(c => char.IsLetterOrDigit(c) || c == '-')) rn.Add(n); }
        }
        if (bl.Count > 0) blocks.Add(new RfcAbnfBlock { Id = Guid.NewGuid(), SectionId = section.Id, RfcNumber = section.RfcNumber, Section = section.Section, AbnfText = string.Join('\n', bl), RuleNames = rn.ToArray() });
    }

    private static IReadOnlyList<NormativeOccurrence> ExtractNormativeKeywords(IReadOnlyList<RfcSection> sections)
    {
        var occ = new List<NormativeOccurrence>();
        foreach (RfcSection section in sections)
        {
            var ls = section.Text.Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                // Track matched character ranges on this line to prevent
                // sub-matches (e.g., "MUST" inside already-matched "MUST NOT").
                var matchedRanges = new List<(int Start, int End)>();

                foreach (string kw in NormativeKeywords)
                {
                    int idx = 0;
                    while ((idx = ls[i].IndexOf(kw, idx, StringComparison.Ordinal)) >= 0)
                    {
                        bool lo = idx == 0 || !char.IsLetterOrDigit(ls[i][idx - 1]);
                        bool ro = idx + kw.Length >= ls[i].Length || !char.IsLetterOrDigit(ls[i][idx + kw.Length]);
                        bool insideMatch = matchedRanges.Any(r => idx >= r.Start && idx < r.End);
                        if (lo && ro && !insideMatch)
                        {
                            occ.Add(new NormativeOccurrence { Id = Guid.NewGuid(), SectionId = section.Id, RfcNumber = section.RfcNumber, Keyword = kw, LineOffset = i });
                            matchedRanges.Add((idx, idx + kw.Length));
                        }
                        idx++;
                    }
                }
            }
        }
        return occ;
    }

    [GeneratedRegex(@"^Obsoletes:\s*(.+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex ObsoletesRegex();
    [GeneratedRegex(@"^Updates:\s*(.+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex UpdatesRegex();
    [GeneratedRegex(@"^Category:\s*(.+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex CategoryRegex();
    [GeneratedRegex(@"^(\w+ \d{4})$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex DateRegex();
    [GeneratedRegex(@"\[Page\s+\d+\]", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex PageFooterRegex();
    [GeneratedRegex(@"^(\d+(?:\.\d+)*\.?|Appendix\s+[A-Z]\.?)\s+(.+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex SectionHeadingRegex();
    [GeneratedRegex(@"^\s{2,}[a-zA-Z][a-zA-Z0-9\-_]*\s*=", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex AbnfLineRegex();
}
