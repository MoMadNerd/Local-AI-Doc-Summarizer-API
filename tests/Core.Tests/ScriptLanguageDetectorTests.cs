using Core.Entities;
using Infrastructure.Language;
using Xunit;

namespace Core.Tests;

public class ScriptLanguageDetectorTests
{
    private static readonly ScriptLanguageDetector Detector = new();

    private const string ArabicSample =
        "\u0647\u0630\u0627 \u0627\u0644\u0645\u0633\u062a\u0646\u062f \u064a\u0634\u0631\u062d "
        + "\u0639\u0645\u0644\u064a\u0629 \u062a\u0644\u062e\u064a\u0635 \u0627\u0644\u0646\u0635\u0648\u0635 "
        + "\u0628\u0627\u0633\u062a\u062e\u062f\u0627\u0645 \u0646\u0645\u0627\u0630\u062c \u0644\u063a\u0648\u064a\u064a\u0629 "
        + "\u0645\u062d\u0644\u064a\u0629. \u062a\u0639\u0645\u0644 \u0647\u0630\u0647 \u0627\u0644\u0623\u062f\u0627\u0629 "
        + "\u062f\u0648\u0646 \u0627\u062a\u0635\u0627\u0644 \u0628\u0627\u0644\u0625\u0646\u062a\u0631\u0646\u062a\u060c "
        + "\u0648\u0644\u0627 \u062a\u063a\u0627\u062f\u0631 \u0627\u0644\u0628\u064a\u0627\u0646\u0627\u062a "
        + "\u0627\u0644\u062c\u0647\u0627\u0632. \u064a\u0645\u0643\u0646 \u0644\u0644\u0645\u0633\u062a\u062e\u062f\u0645\u0648\u0646 "
        + "\u0646\u0634\u0631\u0647\u0627 \u0641\u064a \u0628\u064a\u0626\u0627\u062a\u0647\u0645 \u0627\u0644\u062e\u0627\u0635\u0629. "
        + "\u0647\u0630\u0647 \u0627\u0644\u0645\u0633\u062a\u0646\u062f \u064a\u0634\u0631\u062d \u0639\u0645\u0644\u064a\u0629 "
        + "\u062a\u0644\u062e\u064a\u0635 \u0627\u0644\u0646\u0635\u0648\u0635 \u0628\u0627\u0633\u062a\u062e\u062f\u0627\u0645 "
        + "\u0646\u0645\u0627\u0630\u062c \u0644\u063a\u0648\u064a\u064a\u0629 \u0645\u062d\u0644\u064a\u0629. "
        + "\u062a\u0639\u0645\u0644 \u0647\u0630\u0647 \u0627\u0644\u0623\u062f\u0627\u0629 \u062f\u0648\u0646 "
        + "\u0627\u062a\u0635\u0627\u0644 \u0628\u0627\u0644\u0625\u0646\u062a\u0631\u0646\u062a.";

    private const string EnglishSample =
        "This document explains how to summarize text using local language models. "
        + "The tool runs without an internet connection and keeps data on the machine. "
        + "Teams can deploy it inside their own environment with ease.";

    [Fact]
    public void ArabicText_DetectsArabic()
    {
        Assert.Equal(LanguageCode.Arabic, Detector.Detect(ArabicSample));
    }

    [Fact]
    public void EnglishText_DetectsEnglish()
    {
        Assert.Equal(LanguageCode.English, Detector.Detect(EnglishSample));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrNullText_DetectsUnknown(string? text)
    {
        Assert.Equal(LanguageCode.Unknown, Detector.Detect(text!));
    }

    [Fact]
    public void TooFewScriptCharacters_DetectsUnknown()
    {
        // Below the confidence floor the detector declines to guess.
        Assert.Equal(LanguageCode.Unknown, Detector.Detect("Hi"));
        Assert.Equal(LanguageCode.Unknown, Detector.Detect("\u0645\u0631\u062d\u0628\u0627"));
    }

    [Fact]
    public void PunctuationAndDigits_AreIgnored()
    {
        var text = "2024 - 2025 !!! ??? *** " + EnglishSample;

        Assert.Equal(LanguageCode.English, Detector.Detect(text));
    }

    /// <summary>
    /// A bilingual document usually carries borrowed English technical terms, so the
    /// detector must not flip to Arabic on a bare majority.
    /// </summary>
    [Fact]
    public void LatinDominatedSample_DoesNotBecomeArabic()
    {
        var mixed = EnglishSample + "This report uses API and SDK tooling for summarization.";

        var result = Detector.Detect(mixed);

        Assert.Equal(LanguageCode.English, result);
    }

    [Fact]
    public void HeavilyDominatedArabic_WinsDespiteEnglishTerms()
    {
        var mixed = ArabicSample + ArabicSample + " machine learning model deployment api";

        Assert.Equal(LanguageCode.Arabic, Detector.Detect(mixed));
    }

    [Fact]
    public void VeryLongDocument_UsesOnlyLeadingSample()
    {
        // The detector inspects a bounded leading window, not the whole buffer. Fill
        // that window with Arabic, then append a long English tail: the result must
        // still follow the window, proving the tail is not being counted.
        var arabicRun = string.Concat(Enumerable.Repeat(ArabicSample, 20));
        Assert.True(arabicRun.Length > 4000, "the Arabic run must exceed the sample window");

        var text = arabicRun + string.Concat(Enumerable.Repeat(EnglishSample, 200));

        Assert.Equal(LanguageCode.Arabic, Detector.Detect(text));
    }

    [Fact]
    public void ArabicDiacritics_AreCountedAsArabic()
    {
        const string Diacritics =
            "\u0627\u0644\u0639\u064e\u0631\u064e\u0628\u064e\u064a\u064e\u0651\u0629\u064f "
            + "\u0645\u064e\u0643\u064f\u062a\u064f\u0648\u0628\u064c \u0628\u0650\u0627\u0644\u064e\u062d\u064f\u0631\u064f\u0648\u0641\u0650";

        Assert.Equal(LanguageCode.Arabic, Detector.Detect(Diacritics));
    }

    [Fact]
    public void ArabicPresentationForms_AreCountedAsArabic()
    {
        // Arabic Presentation Forms-B (U+FB50..U+FDFF).
        const string PresentationForms =
            "\uFEB3\uFE91\uFEB3\uFE92\uFE91\uFE93\uFEB3\uFE94\uFE91\uFEB3\uFEB3\uFE91"
            + "\uFE91\uFEB3\uFE92\uFEB3\uFE93\uFEB3\uFEB3\uFE91\uFE91\uFEB3";

        Assert.Equal(LanguageCode.Arabic, Detector.Detect(PresentationForms));
    }

    [Fact]
    public void AccentedLatin_IsCountedAsLatin()
    {
        const string Accented =
            "R\u00e9sum\u00e9 na\u00efvet\u00e9 caf\u00e9 fianc\u00e9 \u00fcber "
            + "\u00c4hnlich fa\u00e7ade se\u00f1or a\u00f1o ma\u00f1ana resumen";

        Assert.Equal(LanguageCode.English, Detector.Detect(Accented));
    }
}