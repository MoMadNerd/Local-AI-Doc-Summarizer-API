using Core.Abstractions;
using Core.Entities;
using Core.Options;

namespace Infrastructure.Language;

/// <summary>
/// Detects the dominant script of a document by counting characters. Deterministic
/// and free: no model call, and no tokens spent asking the model what language to
/// answer in. Counting beats a model here on every axis for a 1.7B model on CPU.
/// </summary>
public sealed class ScriptLanguageDetector : ILanguageDetector
{
    /// <summary>Only a leading sample is inspected; the tail rarely differs.</summary>
    private const int SampleSize = 4000;

    /// <summary>
    /// Below this many script characters the sample is too small to call, and the
    /// detector reports Unknown rather than guessing.
    /// </summary>
    private const int MinimumConfidentCharacters = 20;

    /// <summary>
    /// Arabic must exceed Latin by this margin to win. A bilingual document often
    /// contains borrowed English technical terms, so a bare majority is too eager.
    /// </summary>
    private const double ArabicDominanceRatio = 1.5;

    public LanguageCode Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LanguageCode.Unknown;
        }

        var arabic = 0;
        var latin = 0;

        var limit = Math.Min(text.Length, SampleSize);
        for (var i = 0; i < limit; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                continue;
            }

            if (IsArabic(c))
            {
                arabic++;
            }
            else if (IsLatinLetter(c))
            {
                latin++;
            }
        }

        var total = arabic + latin;
        if (total < MinimumConfidentCharacters)
        {
            return LanguageCode.Unknown;
        }

        if (arabic == 0)
        {
            return LanguageCode.English;
        }

        if (latin == 0)
        {
            return LanguageCode.Arabic;
        }

        // Both scripts present: require a clear margin rather than a bare majority.
        return arabic >= latin * ArabicDominanceRatio
            ? LanguageCode.Arabic
            : LanguageCode.English;
    }

    /// <summary>
    /// True for the Arabic block plus the Arabic diacritics and marks that appear
    /// around letters, which carry no independent linguistic signal.
    /// </summary>
    private static bool IsArabic(char c) =>
        c is >= '\u0600' and <= '\u06FF'   // Arabic
        or >= '\u0750' and <= '\u077F'   // Arabic Supplement
        or >= '\u08A0' and <= '\u08FF'   // Arabic Extended-A
        or >= '\uFB50' and <= '\uFDFF'   // Arabic Presentation Forms-A
        or >= '\uFE70' and <= '\uFEFF';  // Arabic Presentation Forms-B

    /// <summary>
    /// Latin letters only. Extended ranges are included so accented European text
    /// counts as Latin rather than being silently dropped.
    /// </summary>
    private static bool IsLatinLetter(char c) =>
        c is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z'
        or >= 'À' and <= 'ɏ'; // Latin-1 Supplement through Latin Extended-D
}