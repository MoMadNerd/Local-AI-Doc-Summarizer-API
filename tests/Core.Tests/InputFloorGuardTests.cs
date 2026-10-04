using System.Text;
using System.Text.Json;
using Api;
using Api.Middleware;
using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Infrastructure.Chunking;
using Infrastructure.Summarization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

/// <summary>
/// Covers the input floor on the inline-text endpoint: text shorter than
/// <c>SummarizationOptions.MinSummarizableChars</c> must be rejected with 422 and the
/// error code <c>input_too_short</c>, and the model must never be called. The engine
/// is a counting fake, so a regression that reorders the guard behind the model call
/// fails here rather than costing a slow CPU inference.
/// </summary>
public sealed class InputFloorGuardTests
{
    private const int Floor = 200;

    private static string TextOfLength(int length) => new('a', length);

    private sealed class CountingEngine : ISummarizationEngine
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<string> SummarizeAsync(
            string text,
            LanguageCode language,
            SummarizationOptions options,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult("summary");
        }

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private static SummaryPipeline BuildPipeline(
        CountingEngine engine,
        int minSummarizableChars = Floor)
    {
        var summarization = Microsoft.Extensions.Options.Options.Create(new SummarizationOptions
        {
            Model = "test-model",
            MinSummarizableChars = minSummarizableChars,
        });

        var chunking = Microsoft.Extensions.Options.Options.Create(new ChunkingOptions());
        var extraction = Microsoft.Extensions.Options.Options.Create(new ExtractionOptions());

        var summarizer = new MapReduceSummarizer(
            new ParagraphTextChunker(),
            engine,
            NullLogger<MapReduceSummarizer>.Instance);

        return new SummaryPipeline(
            Array.Empty<ITextExtractor>(),
            new AlwaysEnglishDetector(),
            summarizer,
            summarization,
            chunking,
            extraction,
            NullLogger<SummaryPipeline>.Instance);
    }

    private sealed class AlwaysEnglishDetector : ILanguageDetector
    {
        public LanguageCode Detect(string text) => LanguageCode.English;
    }

    private static async Task<(int Status, string Body)> RunThroughMiddlewareAsync(
        Func<Task> throwingEndpoint)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/summarize/text";
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throwingEndpoint(),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Short_input_is_rejected_with_422_and_input_too_short_code()
    {
        var engine = new CountingEngine();
        var pipeline = BuildPipeline(engine);

        var (status, body) = await RunThroughMiddlewareAsync(() =>
            pipeline.SummarizeTextAsync(
                TextOfLength(Floor - 1),
                languageOverride: null,
                maxWords: null,
                chunkSize: null,
                cancellationToken: CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("Input too short", root.GetProperty("title").GetString());
        Assert.Equal("input_too_short", root.GetProperty("code").GetString());
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, root.GetProperty("status").GetInt32());

        var detail = root.GetProperty("detail").GetString()!;
        Assert.Contains("199", detail, StringComparison.Ordinal);
        Assert.Contains("200", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Short_input_never_invokes_the_summarization_engine()
    {
        var engine = new CountingEngine();
        var pipeline = BuildPipeline(engine);

        var exception = await Assert.ThrowsAsync<InputTooShortException>(() =>
            pipeline.SummarizeTextAsync(
                "too short to summarize",
                languageOverride: null,
                maxWords: null,
                chunkSize: null,
                cancellationToken: CancellationToken.None));

        Assert.Equal(InputTooShortException.Code, exception.ErrorCode);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Whitespace_padding_does_not_buy_a_pass()
    {
        var engine = new CountingEngine();
        var pipeline = BuildPipeline(engine);

        // 150 real characters padded out past the floor with spaces.
        var padded = new string('a', 150).PadRight(Floor + 50);

        await Assert.ThrowsAsync<InputTooShortException>(() =>
            pipeline.SummarizeTextAsync(
                padded,
                languageOverride: null,
                maxWords: null,
                chunkSize: null,
                cancellationToken: CancellationToken.None));

        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Input_exactly_at_the_floor_is_accepted_and_reaches_the_engine()
    {
        var engine = new CountingEngine();
        var pipeline = BuildPipeline(engine);

        var response = await pipeline.SummarizeTextAsync(
            TextOfLength(Floor),
            languageOverride: "en",
            maxWords: null,
            chunkSize: null,
            cancellationToken: CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Summary));
        Assert.Equal(1, engine.Calls);
    }

    [Fact]
    public async Task Floor_of_zero_disables_the_guard()
    {
        var engine = new CountingEngine();
        var pipeline = BuildPipeline(engine, minSummarizableChars: 0);

        await pipeline.SummarizeTextAsync(
            "short",
            languageOverride: "en",
            maxWords: null,
            chunkSize: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, engine.Calls);
    }
}