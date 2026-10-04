using Core.Entities;
using Core.Options;

namespace Core.Abstractions;

/// <summary>
/// Produces a summary from text using a local LLM.
/// Implemented over OllamaSharp; kept behind a port so the model host stays
/// swappable without touching the pipeline.
/// </summary>
public interface ISummarizationEngine
{
    /// <summary>
    /// Summarizes a single piece of text.
    /// </summary>
    /// <param name="text">
    /// Text to summarize. Sourced either from a document chunk (map phase) or
    /// from partial summaries (reduce phase), so the prompt must read naturally
    /// in both cases.
    /// </param>
    /// <param name="language">Language to write the summary in.</param>
    /// <param name="options">Inference settings, including output token cap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Plain text. Never JSON.</returns>
    /// <exception cref="SummarizationException">
    /// The model was unreachable, or generation failed.
    /// </exception>
    Task<string> SummarizeAsync(
        string text,
        LanguageCode language,
        SummarizationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports whether the backing model host is reachable and the configured model
    /// is present. Used by the health endpoint.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}