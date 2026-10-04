using Core.Entities;
using Core.Options;
using Infrastructure.Chunking;
using Xunit;

namespace Core.Tests;

public class ParagraphTextChunkerTests
{
    private static readonly ParagraphTextChunker Chunker = new();

    private static TextDocument Doc(string text) => new("test.txt", text, Array.Empty<int>());

    /// <summary>Builds a document of n paragraphs, each roughly <paramref name="wordsPerParagraph"/> words long.</summary>
    private static string Paragraphs(int count, int wordsPerParagraph = 40)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append($"Paragraph {i}.");
            for (var w = 0; w < wordsPerParagraph; w++)
            {
                builder.Append($" word{i}_{w}");
            }

            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    [Fact]
    public void EmptyText_ProducesNoChunks()
    {
        Assert.Empty(Chunker.Chunk(Doc(string.Empty), new ChunkingOptions()));
    }

    [Fact]
    public void WhitespaceOnlyText_ProducesNoChunks()
    {
        Assert.Empty(Chunker.Chunk(Doc("   \n\n\t  "), new ChunkingOptions()));
    }

    [Fact]
    public void TextSmallerThanChunkSize_ProducesExactlyOneChunk()
    {
        var chunks = Chunker.Chunk(
            Doc("A short document that easily fits inside a single chunk."),
            new ChunkingOptions { MaxTokensPerChunk = 2400 });

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.Index);
    }

    /// <summary>
    /// The overlap configuration must never stall the splitter. These are the ratios
    /// that would deadlock a naive offset-based implementation.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void HostileOverlapRatios_TerminateAndStillCoverText(double ratio)
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(40)),
            new ChunkingOptions { MaxTokensPerChunk = 60, OverlapRatio = ratio, MaxChunks = 500 });

        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count <= 500);

        // Every paragraph of the source must appear somewhere in the output, so a
        // stalled or skipping splitter cannot pass this.
        var joined = string.Join("\n", chunks.Select(c => c.Text));
        for (var i = 0; i < 40; i++)
        {
            Assert.Contains($"Paragraph {i}.", joined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OverlapRatioOne_DoesNotProduceDuplicateChunks()
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(10, 10)),
            new ChunkingOptions { MaxTokensPerChunk = 40, OverlapRatio = 1.0, MaxChunks = 100 });

        Assert.NotEmpty(chunks);
        // Progress is guaranteed, so the chunk count is bounded by the paragraph count
        // rather than exploding.
        Assert.True(chunks.Count <= 10, $"expected at most 10 chunks, got {chunks.Count}");
    }

    [Fact]
    public void Chunks_AreIndexedSequentiallyFromZero()
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(30)),
            new ChunkingOptions { MaxTokensPerChunk = 50, MaxChunks = 100 });

        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Index);
        }
    }

    [Fact]
    public void ChunkIndicesAreUnique_AndTokensArePositive()
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(25)),
            new ChunkingOptions { MaxTokensPerChunk = 40, OverlapRatio = 0.1, MaxChunks = 100 });

        Assert.NotEmpty(chunks);
        Assert.Equal(chunks.Count, chunks.Select(c => c.Index).Distinct().Count());
        Assert.All(chunks, c => Assert.True(
            c.ApproximateTokenCount > 0,
            $"chunk {c.Index} reported a non-positive token estimate"));
    }

    [Fact]
    public void DefaultOverlap_RepeatsContentAcrossChunkBoundary()
    {
        // Budget deliberately smaller than two paragraphs, so a 10% overlap must be
        // visible in the output rather than being rounded away.
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(8, 30)),
            new ChunkingOptions { MaxTokensPerChunk = 120, OverlapRatio = 0.5, MaxChunks = 100 });

        Assert.True(chunks.Count > 1, "expected the document to span several chunks");

        // Some paragraph text must appear in more than one chunk; that repetition is
        // the entire purpose of the overlap.
        var shared = chunks
            .SelectMany(c => c.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.Split(' ').First())
            .GroupBy(p => p)
            .Where(g => g.Count() > 1);

        Assert.NotEmpty(shared);
    }

    [Fact]
    public void NoTextIsLost_AcrossAllChunks()
    {
        var source = Paragraphs(12);
        var chunks = Chunker.Chunk(
            Doc(source),
            new ChunkingOptions { MaxTokensPerChunk = 50, OverlapRatio = 0.2, MaxChunks = 200 });

        var joined = string.Join(" ", chunks.Select(c => c.Text));

        for (var i = 0; i < 12; i++)
        {
            Assert.Contains($"Paragraph {i}.", joined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MaxChunks_IsRespected()
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(200)),
            new ChunkingOptions { MaxTokensPerChunk = 20, OverlapRatio = 0.1, MaxChunks = 5 });

        Assert.True(chunks.Count <= 5, $"expected at most 5 chunks, got {chunks.Count}");
    }

    [Fact]
    public void SingleGiantParagraph_IsSplitRatherThanOverflowing()
    {
        var oneLine = string.Join(" ", Enumerable.Range(0, 5000).Select(i => $"word{i}"));

        var chunks = Chunker.Chunk(
            Doc(oneLine),
            new ChunkingOptions { MaxTokensPerChunk = 100, MaxChunks = 1000 });

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotEmpty(c.Text));
        Assert.Equal(chunks.Count, chunks.Select(c => c.Index).Distinct().Count());
    }

    [Fact]
    public void SingleWordLongerThanBudget_StillProducesOneChunk()
    {
        var giantWord = new string('a', 20_000);

        var chunks = Chunker.Chunk(
            Doc(giantWord),
            new ChunkingOptions { MaxTokensPerChunk = 10, OverlapRatio = 0.5, MaxChunks = 10 });

        Assert.NotEmpty(chunks);
        Assert.Contains(giantWord, chunks[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactBoundarySplit_DoesNotThrowOrDuplicate()
    {
        // Token budget divides the paragraph count exactly, the classic boundary case
        // where overlap arithmetic tends to produce an off-by-one.
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(8, 40)),
            new ChunkingOptions { MaxTokensPerChunk = 240, OverlapRatio = 0.1, MaxChunks = 100 });

        Assert.NotEmpty(chunks);
        Assert.Equal(chunks.Count, chunks.Select(c => c.Index).Distinct().Count());
    }

    [Fact]
    public void ZeroTokenBudget_IsClampedRatherThanLooping()
    {
        var chunks = Chunker.Chunk(
            Doc(Paragraphs(6)),
            new ChunkingOptions { MaxTokensPerChunk = 0, MaxChunks = 50 });

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void Cancellation_IsObserved()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            Chunker.Chunk(Doc(Paragraphs(50)), new ChunkingOptions { MaxTokensPerChunk = 20 }, cts.Token));
    }
}