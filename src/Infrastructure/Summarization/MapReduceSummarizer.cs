using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Summarization;

/// <summary>
/// Map-reduce summarization over document chunks.
/// </summary>
/// <remarks>
/// Map and reduce calls are strictly sequential. On a 4-core CPU a 1.7B model is
/// already the bottleneck, so concurrent requests would only add memory pressure
/// and make wall-clock time less predictable without reducing it.
/// </remarks>
public sealed class MapReduceSummarizer
{
    private readonly ITextChunker _chunker;
    private readonly ISummarizationEngine _engine;
    private readonly ILogger<MapReduceSummarizer> _logger;

    public MapReduceSummarizer(
        ITextChunker chunker,
        ISummarizationEngine engine,
        ILogger<MapReduceSummarizer> logger)
    {
        _chunker = chunker;
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Guards the reduce recursion. Beyond this depth the caller receives the folded
    /// summaries rather than an unbounded fold.
    /// </summary>
    private const int MaxReducePasses = 4;

    /// <summary>
    /// Floor for the shrinking reduce budget. Below this the model has too little room
    /// to compress, and the summaries stop shrinking.
    /// </summary>
    private const int MinReduceWords = 40;

    public async Task<SummaryResult> SummarizeAsync(
        TextDocument document,
        SummarizationOptions options,
        ChunkingOptions chunkOptions,
        LanguageCode language,
        CancellationToken cancellationToken = default)
    {
        var chunks = _chunker.Chunk(document, chunkOptions, cancellationToken);

        if (chunks.Count == 0)
        {
            throw new UnsupportedDocumentException(
                $"'{document.Name}' produced no text to summarize.");
        }

        _logger.LogDebug(
            "Map phase: summarizing {ChunkCount} chunk(s) from {FileName} in {Language}",
            chunks.Count,
            document.Name,
            language);

        var summaries = new List<string>(chunks.Count);
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The detected language must reach the engine, otherwise the model falls
            // back to its own default and answers an Arabic document in English.
            var summary = await _engine.SummarizeAsync(
                chunk.Text,
                language,
                options,
                cancellationToken);

            summaries.Add(summary);
        }

        var final = await ReduceAsync(summaries, language, options, cancellationToken);

        return new SummaryResult(
            final,
            language,
            chunks.Count,
            document.CharacterCount,
            options.Model,
            Array.Empty<string>());
    }

    private async Task<string> ReduceAsync(
        IReadOnlyList<string> summaries,
        LanguageCode language,
        SummarizationOptions options,
        CancellationToken cancellationToken)
    {
        var current = summaries.ToList();
        var targetWords = options.MaxSummaryWords;

        for (var pass = 1; pass < MaxReducePasses; pass++)
        {
            if (current.Count == 1)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Each pass must ask for a strictly shorter summary. A small model asked to
            // "summarize these five summaries" at the same target length will simply
            // return five summaries of that length again, so without a shrinking
            // budget the reduce phase never converges and the caller receives
            // near-duplicate paragraphs joined together.
            targetWords = Math.Max(MinReduceWords, targetWords / 2);

            var passOptions = new SummarizationOptions
            {
                Model = options.Model,
                OllamaEndpoint = options.OllamaEndpoint,
                Temperature = options.Temperature,
                NumCtx = options.NumCtx,
                Think = options.Think,
                MaxOutputTokens = options.MaxOutputTokens,
                Timeout = options.Timeout,
                MaxSummaryWords = targetWords,
            };

            _logger.LogDebug(
                "Reduce pass {Pass}: folding {Count} partial summaries into ~{Target} words",
                pass,
                current.Count,
                targetWords);

            // All partials are folded in a single call so the list shrinks by roughly
            // half each pass. Summarizing each partial separately, as the map phase
            // does, would keep the count constant and never converge.
            var combined = string.Join("\n\n", current);
            var folded = await _engine.SummarizeAsync(
                combined,
                language,
                passOptions,
                cancellationToken);

            current = new List<string> { folded };
        }

        // If the fold limit is reached with several summaries outstanding, joining them
        // preserves the content. Returning only the first would silently discard the
        // rest of the document.
        return current.Count == 1
            ? current[0]
            : string.Join("\n\n", current);
    }
}