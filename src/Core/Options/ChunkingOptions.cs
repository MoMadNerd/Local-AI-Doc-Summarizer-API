namespace Core.Options;

/// <summary>
/// Controls how extracted text is split before summarization.
/// </summary>
public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";

    /// <summary>
    /// Token budget per chunk. Kept below <see cref="SummarizationOptions.NumCtx"/>
    /// so that prompt, chunk and output all fit the model's context window.
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 2400;

    /// <summary>
    /// Fraction of each chunk repeated in the next one, so a fact that straddles
    /// a chunk boundary is still seen whole by the map phase. Expressed as a
    /// fraction of the chunk budget, between 0 and 1.
    /// </summary>
    public double OverlapRatio { get; set; } = 0.1;

    /// <summary>
    /// Upper bound on map-phase calls per document, a guard against pathological
    /// input. Beyond this the remaining chunks are folded into the reduce phase.
    /// </summary>
    public int MaxChunks { get; set; } = 64;
}