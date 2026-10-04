using System.Diagnostics;
using Core.Abstractions;
using Core.Dtos;
using Core.Entities;
using Core.Options;
using Infrastructure.Summarization;
using Microsoft.Extensions.Options;

namespace Api;

/// <summary>
/// Orchestrates the full pipeline: pick an extractor, extract, detect the language,
/// chunk, summarize, and shape the response envelope.
/// </summary>
public sealed class SummaryPipeline
{
    private readonly IEnumerable<ITextExtractor> _extractors;
    private readonly ILanguageDetector _languageDetector;
    private readonly MapReduceSummarizer _summarizer;
    private readonly SummarizationOptions _summarizationOptions;
    private readonly ChunkingOptions _chunkingOptions;
    private readonly ExtractionOptions _extractionOptions;
    private readonly ILogger<SummaryPipeline> _logger;

    public SummaryPipeline(
        IEnumerable<ITextExtractor> extractors,
        ILanguageDetector languageDetector,
        MapReduceSummarizer summarizer,
        IOptions<SummarizationOptions> summarizationOptions,
        IOptions<ChunkingOptions> chunkingOptions,
        IOptions<ExtractionOptions> extractionOptions,
        ILogger<SummaryPipeline> logger)
    {
        _extractors = extractors;
        _languageDetector = languageDetector;
        _summarizer = summarizer;
        _summarizationOptions = summarizationOptions.Value;
        _chunkingOptions = chunkingOptions.Value;
        _extractionOptions = extractionOptions.Value;
        _logger = logger;
    }

    public async Task<SummarizeResponse> SummarizeTextAsync(
        string text,
        string? languageOverride,
        int? maxWords,
        int? chunkSize,
        CancellationToken cancellationToken)
    {
        var document = new TextDocument("(inline text)", text, Array.Empty<int>());
        return await RunAsync(document, Array.Empty<string>(), languageOverride, maxWords, chunkSize, cancellationToken);
    }

    public async Task<SummarizeResponse> SummarizeFileAsync(
        Stream stream,
        string fileName,
        string? languageOverride,
        int? maxWords,
        int? chunkSize,
        CancellationToken cancellationToken)
    {
        if (!SupportedFormats.IsSupported(fileName))
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' is not a supported format. Supported: "
                + $"{string.Join(", ", SupportedFormats.Extensions.OrderBy(e => e))}.");
        }

        var extractor = _extractors.FirstOrDefault(e => e.Supports(fileName))
            ?? throw new UnsupportedDocumentException(
                $"No extractor is registered for '{fileName}'.");

        var extraction = await extractor.ExtractAsync(stream, fileName, cancellationToken);

        return await RunAsync(
            extraction.Document,
            extraction.Warnings,
            languageOverride,
            maxWords,
            chunkSize,
            cancellationToken);
    }

    private async Task<SummarizeResponse> RunAsync(
        TextDocument document,
        IReadOnlyList<string> warnings,
        string? languageOverride,
        int? maxWords,
        int? chunkSize,
        CancellationToken cancellationToken)
    {
        if (document.CharacterCount == 0)
        {
            throw new UnsupportedDocumentException(
                $"'{document.Name}' contained no readable text.");
        }

        var language = ResolveLanguage(languageOverride, document.Text);

        // Per-request overrides are applied on a copy so a request can never mutate
        // the shared singleton options.
        var summarizationOptions = new SummarizationOptions
        {
            Model = _summarizationOptions.Model,
            OllamaEndpoint = _summarizationOptions.OllamaEndpoint,
            Temperature = _summarizationOptions.Temperature,
            NumCtx = _summarizationOptions.NumCtx,
            Think = _summarizationOptions.Think,
            MaxOutputTokens = _summarizationOptions.MaxOutputTokens,
            Timeout = _summarizationOptions.Timeout,
            MaxSummaryWords = maxWords ?? _summarizationOptions.MaxSummaryWords,
        };
        var chunkOptions = ChunkSizeOverride(chunkSize);

        var stopwatch = Stopwatch.StartNew();
        var result = await _summarizer.SummarizeAsync(
            document, summarizationOptions, chunkOptions, language, cancellationToken);
        stopwatch.Stop();

        // SummaryResult is a record, so the language is restated here to keep the
        // envelope authoritative even if the engine path changes later.
        var enriched = result with { DetectedLanguage = language };

        _logger.LogInformation(
            "Summarized {FileName}: {SourceChars} chars -> {SummaryChars} chars in {Elapsed:0.0}s via {Model}",
            document.Name,
            document.CharacterCount,
            enriched.Summary.Length,
            stopwatch.Elapsed.TotalSeconds,
            summarizationOptions.Model);

        return SummarizeResponse.FromResult(enriched, stopwatch.Elapsed.TotalSeconds);
    }

    private ChunkingOptions ChunkSizeOverride(int? chunkSize) =>
        chunkSize is null
            ? _chunkingOptions
            : new ChunkingOptions
            {
                MaxTokensPerChunk = chunkSize.Value,
                OverlapRatio = _chunkingOptions.OverlapRatio,
                MaxChunks = _chunkingOptions.MaxChunks,
            };

    private LanguageCode ResolveLanguage(string? overrideValue, string text)
    {
        if (string.IsNullOrWhiteSpace(overrideValue) || overrideValue == "auto")
        {
            return _languageDetector.Detect(text);
        }

        return overrideValue switch
        {
            "ar" => LanguageCode.Arabic,
            "en" => LanguageCode.English,
            _ => _languageDetector.Detect(text),
        };
    }
}
