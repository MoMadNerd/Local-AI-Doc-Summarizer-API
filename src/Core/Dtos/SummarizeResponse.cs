using Core.Entities;

namespace Core.Dtos;

/// <summary>
/// The JSON envelope returned by both summarize endpoints. Structured output lives
/// here only — the model itself returns plain text, which is why this shape is built
/// by the API rather than requested from the LLM.
/// </summary>
/// <param name="Summary">Summary text, in the source document's language.</param>
/// <param name="DetectedLanguage">Detected language code: ar, en, or unknown.</param>
/// <param name="ChunkCount">Chunks produced by the map phase.</param>
/// <param name="SourceCharacters">Character count of the extracted document.</param>
/// <param name="Model">Model identifier used.</param>
/// <param name="ElapsedSeconds">Wall-clock duration of the whole request.</param>
/// <param name="Warnings">Non-fatal extraction or summarization warnings.</param>
public sealed record SummarizeResponse(
    string Summary,
    string DetectedLanguage,
    int ChunkCount,
    int SourceCharacters,
    string Model,
    double ElapsedSeconds,
    IReadOnlyList<string> Warnings)
{
    public static SummarizeResponse FromResult(
        SummaryResult result,
        double elapsedSeconds) =>
        new(
            result.Summary,
            ToCode(result.DetectedLanguage),
            result.ChunkCount,
            result.SourceCharacters,
            result.Model,
            Math.Round(elapsedSeconds, 3),
            result.Warnings);

    /// <summary>
    /// Renders the enum as the short lowercase code used in the JSON contract.
    /// </summary>
    public static string ToCode(LanguageCode language) => language switch
    {
        LanguageCode.Arabic => "ar",
        LanguageCode.English => "en",
        _ => "unknown",
    };
}