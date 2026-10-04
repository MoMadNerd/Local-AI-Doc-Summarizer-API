namespace Core.Entities;

/// <summary>
/// One unit of work in the map phase of map-reduce summarization.
/// </summary>
/// <param name="Index">Zero-based position of this chunk in the document.</param>
/// <param name="Text">The chunk's text, including any overlap with its neighbours.</param>
/// <param name="ApproximateTokenCount">
/// Estimated token count, used to decide whether the chunk still fits the model's
/// context window. Estimated rather than exact because a real tokenizer would add
/// a dependency to Core.
/// </param>
public sealed record TextChunk(
    int Index,
    string Text,
    int ApproximateTokenCount);