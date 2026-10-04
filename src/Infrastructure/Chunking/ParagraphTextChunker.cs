using System.Text;
using System.Text.RegularExpressions;
using Core.Abstractions;
using Core.Entities;
using Core.Options;

namespace Infrastructure.Chunking;

/// <summary>
/// Splits a document into overlapping chunks for the map phase of map-reduce
/// summarization.
/// </summary>
/// <remarks>
/// The implementation advances through the paragraph list rather than slicing the
/// text by offset. That choice is deliberate: overlap is expressed as "how far back
/// the next chunk starts", and the advance distance is always clamped to at least
/// one paragraph. Overlap therefore cannot exceed the current chunk and stall the
/// loop, which is the failure mode a naive offset-based splitter hits when the
/// requested overlap meets or passes the chunk size.
/// </remarks>
public sealed partial class ParagraphTextChunker : ITextChunker
{
    private readonly ITokenEstimator _estimator;

    public ParagraphTextChunker() : this(new HeuristicTokenEstimator())
    {
    }

    public ParagraphTextChunker(ITokenEstimator estimator) => _estimator = estimator;

    [GeneratedRegex(@"(?<=[.!?؟۔।])\s+|\n+", RegexOptions.Compiled)]
    private static partial Regex SentenceBoundary();

    public IReadOnlyList<TextChunk> Chunk(
        TextDocument document,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            return Array.Empty<TextChunk>();
        }

        var budget = Math.Max(1, options.MaxTokensPerChunk);
        var maxChunks = Math.Max(1, options.MaxChunks);

        // A ratio of 1 or more would mean "repeat everything", which cannot make
        // progress. Clamping here also neutralises a misconfigured negative value.
        var overlapRatio = Math.Clamp(options.OverlapRatio, 0d, 0.9d);

        var paragraphs = BuildParagraphs(document.Text, budget);
        var chunks = new List<TextChunk>();

        var index = 0;
        while (index < paragraphs.Count && chunks.Count < maxChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (text, nextIndex) = TakeChunk(paragraphs, index, budget, overlapRatio);
            if (text.Length == 0)
            {
                break;
            }

            chunks.Add(new TextChunk(
                chunks.Count,
                text,
                _estimator.Estimate(text)));

            // nextIndex is always greater than index, so the loop terminates.
            if (nextIndex <= index)
            {
                nextIndex = index + 1;
            }

            if (nextIndex >= paragraphs.Count)
            {
                break;
            }

            index = nextIndex;
        }

        return chunks;
    }

    /// <summary>
    /// Accumulates whole paragraphs from <paramref name="start"/> while they fit the
    /// budget, then returns the chunk text and where the next chunk should start.
    /// </summary>
    private (string Text, int NextIndex) TakeChunk(
        IReadOnlyList<string> paragraphs,
        int start,
        int budget,
        double overlapRatio)
    {
        var builder = new StringBuilder();
        var tokens = 0;
        var end = start;

        for (var i = start; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphTokens = _estimator.Estimate(paragraph);

            if (i > start && tokens + paragraphTokens > budget)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(paragraph);
            tokens += paragraphTokens;
            end = i + 1;

            if (tokens >= budget)
            {
                break;
            }
        }

        // Always consume at least one paragraph, so a single oversized paragraph
        // cannot produce a zero-length chunk.
        if (end <= start)
        {
            end = start + 1;
        }

        var overlapBudget = (int)(budget * overlapRatio);
        var next = NextStartIndex(paragraphs, start, end, overlapBudget);

        return (builder.ToString(), next);
    }

    /// <summary>
    /// Walks back from the end of the chunk to find where the next one starts, so that
    /// roughly <c>overlapBudget</c> tokens are repeated. The result is clamped into
    /// <c>[start + 1, end]</c>, which is what guarantees forward progress.
    /// </summary>
    private static int NextStartIndex(
        IReadOnlyList<string> paragraphs,
        int start,
        int end,
        int overlapBudget)
    {
        if (overlapBudget <= 0 || end - start <= 1)
        {
            return end;
        }

        var carried = 0;
        var candidate = end;

        for (var i = end - 1; i > start; i--)
        {
            var paragraphTokens = EstimateTokens(paragraphs[i]);
            if (carried + paragraphTokens > overlapBudget)
            {
                break;
            }

            carried += paragraphTokens;
            candidate = i;
        }

        // Overlap must be strictly smaller than the chunk just emitted; otherwise the
        // cursor would not advance and the loop would never terminate.
        if (candidate >= end)
        {
            candidate = end - 1;
        }

        return candidate;
    }

    private static int EstimateTokens(string text) =>
        Math.Max(1, (int)Math.Ceiling(text.Length / 4d));

    /// <summary>
    /// Splits text into paragraphs, first on blank lines and then on sentence
    /// boundaries for any single unit too large to ever fit the budget.
    /// </summary>
    private List<string> BuildParagraphs(string text, int budget)
    {
        var raw = text
            .Split(new[] { "\n\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

        var paragraphs = new List<string>();
        foreach (var paragraph in raw)
        {
            // A paragraph past the budget on its own is split on sentences; if a
            // single sentence still does not fit, it is split on word boundaries.
            if (EstimateTokens(paragraph) <= budget)
            {
                paragraphs.Add(paragraph);
                continue;
            }

            paragraphs.AddRange(SplitOversized(paragraph, budget));
        }

        return paragraphs;
    }

    private IEnumerable<string> SplitOversized(string paragraph, int budget)
    {
        var sentences = SentenceBoundary()
            .Split(paragraph)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

        var current = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (EstimateTokens(sentence) > budget)
            {
                // Flush before splitting the runaway sentence on word boundaries.
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                foreach (var piece in SplitByWords(sentence, budget))
                {
                    yield return piece;
                }

                continue;
            }

            var projected = (current.Length == 0 ? 0 : _estimator.Estimate(current.ToString()))
                            + _estimator.Estimate(sentence);

            if (current.Length > 0 && projected > budget)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> SplitByWords(string sentence, int budget)
    {
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && EstimateTokens(current.ToString() + " " + word) > budget)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}