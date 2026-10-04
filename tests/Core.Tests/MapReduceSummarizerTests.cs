using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Infrastructure.Chunking;
using Infrastructure.Summarization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Core.Tests;

/// <summary>
/// Exercises the map-reduce pipeline against a fake engine, so the orchestration is
/// verified without a model running. The real engine is covered separately against
/// Ollama in the requests.http smoke tests.
/// </summary>
public class MapReduceSummarizerTests
{
    /// <summary>
    /// Records every call and returns a deterministic, shrinking summary so the
    /// reduce phase converges instead of looping forever.
    /// </summary>
    private sealed class FakeEngine : ISummarizationEngine
    {
        private readonly int _shrinkTo;
        private int _inFlight;

        public FakeEngine(int shrinkTo = 1)
        {
            _shrinkTo = shrinkTo;
            Calls = new List<string>();
        }

        public List<string> Calls { get; }

        /// <summary>Maximum engine calls observed running at the same time.</summary>
        public int MaxConcurrency { get; private set; }

        public Task<string> SummarizeAsync(
            string text,
            LanguageCode language,
            SummarizationOptions options,
            CancellationToken cancellationToken = default)
        {
            // Tracks overlap so a concurrent map phase is detectable. The list itself
            // is not thread-safe, which would otherwise turn a race into a flaky test
            // instead of a clear assertion.
            var current = Interlocked.Increment(ref _inFlight);
            MaxConcurrency = Math.Max(MaxConcurrency, current);

            Calls.Add(text);

            var length = Math.Max(_shrinkTo, Math.Min(text.Length, 24));
            var result = string.Join(' ', Enumerable.Repeat("s", length));

            Interlocked.Decrement(ref _inFlight);
            return Task.FromResult(result);
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FailingEngine : ISummarizationEngine
    {
        public Task<string> SummarizeAsync(
            string text,
            LanguageCode language,
            SummarizationOptions options,
            CancellationToken cancellationToken = default) =>
            throw new SummarizationException("backend down");

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static TextDocument Document(string text) =>
        new("test.txt", text, Array.Empty<int>());

    private static string Paragraphs(int count, int words = 40)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append($"Paragraph {i}.");
            builder.Append(string.Concat(Enumerable.Repeat($" word{i}", words)));
            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    private static MapReduceSummarizer Build(ISummarizationEngine engine) =>
        new(new ParagraphTextChunker(), engine, NullLogger<MapReduceSummarizer>.Instance);

    [Fact]
    public async Task SingleChunk_ProducesResultWithoutReducePass()
    {
        var engine = new FakeEngine();
        var result = await Build(engine).SummarizeAsync(
            Document("A short note."),
            new SummarizationOptions(),
            new ChunkingOptions { MaxTokensPerChunk = 2400 }, LanguageCode.Unknown);

        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(1, engine.Calls.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    [Fact]
    public async Task MultipleChunks_AreEachSummarizedInTheMapPhase()
    {
        var engine = new FakeEngine();
        var result = await Build(engine).SummarizeAsync(
            Document(Paragraphs(12)),
            new SummarizationOptions(),
            new ChunkingOptions { MaxTokensPerChunk = 60, MaxChunks = 200 }, LanguageCode.Unknown);

        Assert.True(result.ChunkCount > 1, "expected the document to be split");

        // The engine sees one call per chunk in the map phase, then the reduce phase
        // folds the partials together, so the total exceeds the chunk count.
        Assert.True(
            engine.Calls.Count >= result.ChunkCount,
            $"expected at least {result.ChunkCount} calls, saw {engine.Calls.Count}");

        // The leading calls must be the chunks themselves, in document order.
        Assert.Contains("Paragraph 0.", engine.Calls[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The reduce phase must combine partials into a single summary. Summarizing each
    /// partial separately would keep the count constant, so the result would arrive as
    /// a run of near-duplicate paragraphs.
    /// </summary>
    [Fact]
    public async Task ReducePhase_FoldsPartialsIntoOneSummary()
    {
        var engine = new FakeEngine();

        var result = await Build(engine).SummarizeAsync(
            Document(Paragraphs(12)),
            new SummarizationOptions(),
            new ChunkingOptions { MaxTokensPerChunk = 60, MaxChunks = 200 },
            LanguageCode.Unknown);

        Assert.True(result.ChunkCount > 1, "expected several chunks to fold");

        // No doubled newline means the reduce phase produced one contiguous summary
        // rather than several stitched-together partials.
        Assert.DoesNotContain("\n\n", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReducePhase_ShrinksTheWordBudgetEachPass()
    {
        var engine = new RecordingBudgetEngine();

        await Build(engine).SummarizeAsync(
            Document(Paragraphs(12)),
            new SummarizationOptions { MaxSummaryWords = 300 },
            new ChunkingOptions { MaxTokensPerChunk = 60, MaxChunks = 200 },
            LanguageCode.Unknown);

        // The map phase uses the configured budget; every later call must be smaller,
        // otherwise the summaries never compress.
        Assert.True(engine.Budgets.Count > 1, "expected a reduce call to follow the map phase");

        for (var i = 1; i < engine.Budgets.Count; i++)
        {
            Assert.True(
                engine.Budgets[i] <= engine.Budgets[i - 1],
                $"budget grew at call {i}: {engine.Budgets[i - 1]} -> {engine.Budgets[i]}");
        }
    }

    private sealed class RecordingBudgetEngine : ISummarizationEngine
    {
        public List<int> Budgets { get; } = new();

        public Task<string> SummarizeAsync(
            string text,
            LanguageCode language,
            SummarizationOptions options,
            CancellationToken cancellationToken = default)
        {
            Budgets.Add(options.MaxSummaryWords);
            return Task.FromResult("folded summary");
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task Result_CarriesSourceAndModelMetadata()
    {
        var document = Document(Paragraphs(3));
        var options = new SummarizationOptions { Model = "test-model:1b" };

        var result = await Build(new FakeEngine()).SummarizeAsync(
            document, options, new ChunkingOptions(), LanguageCode.Arabic);

        Assert.Equal(document.CharacterCount, result.SourceCharacters);
        Assert.Equal("test-model:1b", result.Model);
    }

    /// <summary>
    /// The detected language must reach the engine, otherwise an Arabic document is
    /// answered in English. This is the regression guard for that defect.
    /// </summary>
    [Fact]
    public async Task RequestedLanguage_IsPassedToTheEngine()
    {
        var engine = new RecordingLanguageEngine();

        var result = await Build(engine).SummarizeAsync(
            Document(Paragraphs(6)),
            new SummarizationOptions(),
            new ChunkingOptions { MaxTokensPerChunk = 40, MaxChunks = 100 },
            LanguageCode.Arabic);

        Assert.NotEmpty(engine.Languages);
        Assert.All(engine.Languages, l => Assert.Equal(LanguageCode.Arabic, l));
        Assert.Equal(LanguageCode.Arabic, result.DetectedLanguage);
    }

    private sealed class RecordingLanguageEngine : ISummarizationEngine
    {
        public List<LanguageCode> Languages { get; } = new();

        public Task<string> SummarizeAsync(
            string text,
            LanguageCode language,
            SummarizationOptions options,
            CancellationToken cancellationToken = default)
        {
            Languages.Add(language);
            return Task.FromResult("summary");
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task EmptyDocument_ThrowsUnsupportedDocument()
    {
        await Assert.ThrowsAsync<UnsupportedDocumentException>(() =>
            Build(new FakeEngine()).SummarizeAsync(
                Document("   "),
                new SummarizationOptions(),
                new ChunkingOptions(),
                LanguageCode.Unknown));
    }

    [Fact]
    public async Task EngineFailure_PropagatesRatherThanReturningEmptySummary()
    {
        await Assert.ThrowsAsync<SummarizationException>(() =>
            Build(new FailingEngine()).SummarizeAsync(
                Document(Paragraphs(4)),
                new SummarizationOptions(),
                new ChunkingOptions { MaxTokensPerChunk = 40 }, LanguageCode.Unknown));
    }

    [Fact]
    public async Task Cancellation_IsObserved()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Build(new FakeEngine()).SummarizeAsync(
                Document(Paragraphs(20)),
                new SummarizationOptions(),
                new ChunkingOptions { MaxTokensPerChunk = 20 },
                LanguageCode.Unknown,
                cts.Token));
    }

    [Fact]
    public async Task MapPhase_IsStrictlySequential()
    {
        // The reduce phase feeds identical partial summaries back through the engine,
        // so duplicate call text is expected and is not evidence of a race. Overlap is
        // measured directly instead.
        var engine = new FakeEngine();

        await Build(engine).SummarizeAsync(
            Document(Paragraphs(10)),
            new SummarizationOptions(),
            new ChunkingOptions { MaxTokensPerChunk = 50, MaxChunks = 100 },
            LanguageCode.Unknown);

        Assert.Equal(1, engine.MaxConcurrency);
    }
}
