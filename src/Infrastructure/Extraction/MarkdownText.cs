using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.Extraction;

/// <summary>
/// Removes Markdown syntax so the summarizer receives prose. Fenced code blocks are
/// stripped of their fences but kept, since their contents are usually the substance
/// of a technical document.
/// </summary>
internal static partial class MarkdownText
{
    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*", RegexOptions.Multiline)]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^\s{0,3}>\s?", RegexOptions.Multiline)]
    private static partial Regex BlockQuotePrefix();

    [GeneratedRegex(@"^\s*([-*_])\s*\1\s*\1[\s\S]*?$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"`{1,3}([^`]*)`{1,3}", RegexOptions.Compiled)]
    private static partial Regex InlineCodeFence();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"^\s{0,3}([-*+]|\d+\.)\s+", RegexOptions.Multiline)]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"(\*\*|__)(.*?)\1", RegexOptions.Compiled)]
    private static partial Regex Bold();

    [GeneratedRegex(@"(\*|_)(?=\S)(.*?\S)\1", RegexOptions.Compiled)]
    private static partial Regex Italic();

    public static string Strip(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
                           .Replace('\r', '\n');

        text = HorizontalRule().Replace(text, string.Empty);
        text = HeadingPrefix().Replace(text, string.Empty);
        text = BlockQuotePrefix().Replace(text, string.Empty);
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = ListMarker().Replace(text, string.Empty);
        text = Bold().Replace(text, "$2");
        text = Italic().Replace(text, "$2");
        text = InlineCodeFence().Replace(text, "$1");

        // Unclosed code fences are common in truncated documents; drop only the fence
        // markers so their contents survive.
        text = text.Replace("```", string.Empty, StringComparison.Ordinal);

        return text;
    }

    /// <summary>
    /// Collapses runs of blank lines and trims trailing spaces per line, so chunking
    /// sees consistent paragraph separators.
    /// </summary>
    public static string NormalizeNewlines(string text)
    {
        var builder = new StringBuilder(text.Length);
        var blankRun = 0;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
            {
                blankRun++;
                if (blankRun > 1)
                {
                    continue;
                }

                builder.Append('\n');
                continue;
            }

            blankRun = 0;
            builder.Append(trimmed).Append('\n');
        }

        return builder.ToString().Trim();
    }
}