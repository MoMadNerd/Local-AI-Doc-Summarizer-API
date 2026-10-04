using System.Text;

namespace Infrastructure.Extraction;

/// <summary>
/// Text clean-up shared by every extractor. PDF text layers and DOCX runs both
/// arrive with hard-wrapped lines and stray whitespace that would otherwise inflate
/// the chunk budget and fragment paragraphs.
/// </summary>
internal static class TextNormalizer
{
    private const char SoftHyphen = '\u00AD';

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ')    // non-breaking space
            .Replace('\u200B', ' ')    // zero-width space
            .Replace('\uFEFF', ' ')   // BOM mid-stream
            .Replace(SoftHyphen, ' '); // soft hyphen indicates a discretionary break

        normalized = MarkdownText.NormalizeNewlines(normalized);
        normalized = RejoinSoftWrappedLines(normalized);

        return CollapseInternalSpaces(normalized);
    }

    /// <summary>
    /// Joins lines broken by hard wrapping, keeping genuine paragraph breaks.
    /// A trailing hyphen followed by a lowercase continuation is rejoined without a
    /// space, since it is almost always a compound word split mid-syllable.
    /// </summary>
    private static string RejoinSoftWrappedLines(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSeparator = '\0'; // '\0' means "no separator yet"

        foreach (var line in text.Split('\n'))
        {
            var current = line.Trim();

            if (current.Length == 0)
            {
                // Blank line ends the paragraph. Flush whatever separator is pending.
                if (pendingSeparator != '\0')
                {
                    builder.Append(pendingSeparator).Append('\n');
                    pendingSeparator = '\0';
                }

                continue;
            }

            if (pendingSeparator == '\0')
            {
                builder.Append(current);
            }
            else if (pendingSeparator == '-')
            {
                // Compound word: drop the hyphen, no space.
                builder.Append(current);
            }
            else
            {
                builder.Append(' ').Append(current);
            }

            // Decide the separator to insert before the next line.
            pendingSeparator = EndsWithSplitHyphen(current) ? '-' : ' ';
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when a trailing hyphen is a wrap artefact rather than punctuation.
    /// Requires a letter immediately before it: a hyphen after a space or a digit
    /// is a genuine dash, while a letter means the word was broken mid-syllable
    /// ("inter-" + "national").
    /// </summary>
    private static bool EndsWithSplitHyphen(string line)
    {
        if (line.Length < 2)
        {
            return false;
        }

        if (line[^1] is not ('-' or '\u2010'))
        {
            return false;
        }

        return char.IsLetter(line[^2]);
    }

    private static string CollapseInternalSpaces(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            while (line.Contains("  ", StringComparison.Ordinal))
            {
                line = line.Replace("  ", " ", StringComparison.Ordinal);
            }

            lines[i] = line;
        }

        return string.Join('\n', lines).Trim();
    }
}