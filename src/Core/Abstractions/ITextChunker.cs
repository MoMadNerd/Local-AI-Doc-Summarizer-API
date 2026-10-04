using Core.Entities;
using Core.Options;

namespace Core.Abstractions;

/// <summary>
/// Splits a document into chunks that each fit the model's context window,
/// for the map phase of map-reduce summarization.
/// </summary>
public interface ITextChunker
{
    /// <summary>
    /// Splits <paramref name="document"/> into ordered chunks.
    /// </summary>
    /// <param name="document">The extracted document.</param>
    /// <param name="options">Chunk budget and overlap settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// At least one chunk for any non-empty document. Implementations must not
    /// drop text: consecutive chunks overlap, and the union of all chunks covers
    /// the whole document.
    /// </returns>
    IReadOnlyList<TextChunk> Chunk(
        TextDocument document,
        ChunkingOptions options,
        CancellationToken cancellationToken = default);
}