namespace Core.Entities;

/// <summary>
/// Outcome of a summarization request. This is the in-memory form of the JSON
/// envelope returned by the API — the model itself only ever emits plain text.
/// </summary>
/// <param name="Summary">The summary text, in the source language.</param>
/// <param name="DetectedLanguage">Language the summary was written in.</param>
/// <param name="ChunkCount">How many chunks the map phase produced.</param>
/// <param name="SourceCharacters">Character count of the extracted document.</param>
/// <param name="Model">Model identifier used, so output is never mistaken for a larger model's work.</param>
/// <param name="Warnings">Non-fatal extraction or summarization warnings.</param>
public sealed record SummaryResult(
    string Summary,
    LanguageCode DetectedLanguage,
    int ChunkCount,
    int SourceCharacters,
    string Model,
    IReadOnlyList<string> Warnings);