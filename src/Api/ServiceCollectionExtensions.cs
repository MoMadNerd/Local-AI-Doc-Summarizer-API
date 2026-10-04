using Core.Abstractions;
using Core.Options;
using Infrastructure.Chunking;
using Infrastructure.Extraction;
using Infrastructure.Language;
using Infrastructure.Ollama;
using Infrastructure.Summarization;

namespace Api;

/// <summary>
/// Registers the pipeline. Kept apart from Program.cs so the composition root stays
/// readable and this wiring can be asserted in a test.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        // Options are bound once and shared, so the engine and the endpoints cannot
        // disagree about the model, timeout or chunk budget.
        services.Configure<SummarizationOptions>(configuration.GetSection(SummarizationOptions.SectionName));
        services.Configure<ChunkingOptions>(configuration.GetSection(ChunkingOptions.SectionName));
        services.Configure<ExtractionOptions>(configuration.GetSection(ExtractionOptions.SectionName));

        services.AddSingleton(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SummarizationOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ChunkingOptions>>().Value);
        services.AddSingleton(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExtractionOptions>>().Value);

        services.AddSingleton<ITextChunker, ParagraphTextChunker>();
        services.AddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
        services.AddSingleton<ILanguageDetector, ScriptLanguageDetector>();

        // ITextExtractor is resolved as a collection so each request can pick the
        // extractor matching the uploaded extension.
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();

        services.AddSingleton<ISummarizationEngine, OllamaSummarizationEngine>();
        services.AddSingleton<MapReduceSummarizer>();

        return services;
    }
}