using Api;
using Api.Middleware;
using Core.Abstractions;
using Core.Dtos;
using Core.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Local AI Doc Summarizer", Version = "v1" });
});

var extractionOptions = builder.Configuration
    .GetSection(ExtractionOptions.SectionName)
    .Get<ExtractionOptions>() ?? new ExtractionOptions();

// Kestrel enforces MaxRequestBodySize before the request reaches a handler, so it must
// be set to the same limit. Leaving it at the 30 MB default meant an oversized upload
// was rejected by Kestrel with a 400 long before our own 413 check could run.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = extractionOptions.MaxFileSizeBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    // The multipart reader has its own limit; without this it would reject a large
    // body independently of Kestrel and with a different status code.
    options.MultipartBodyLengthLimit = extractionOptions.MaxFileSizeBytes;
});

builder.Services.AddDocumentPipeline(builder.Configuration);
builder.Services.AddScoped<SummaryPipeline>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var summaries = app.MapGroup("/api/v1");

summaries.MapPost("/summarize/text", async (
        SummarizeTextRequest request,
        SummaryPipeline pipeline,
        CancellationToken cancellationToken) =>
{
    var response = await pipeline.SummarizeTextAsync(
        request.Text,
        request.Language,
        request.MaxWords,
        request.ChunkSize,
        cancellationToken);

    return Results.Ok(response);
})
.WithName("SummarizeText")
.WithSummary("Summarize raw text")
.WithTags("Summarize");

summaries.MapPost("/summarize", async (
        HttpContext context,
        SummaryPipeline pipeline,
        IOptions<ExtractionOptions> extractionOptions,
        CancellationToken cancellationToken) =>
{
    var form = await context.Request.ReadFormAsync(cancellationToken);

    // Guard before the pipeline: a missing or empty upload is a malformed request, not an
    // unsupported document, and must never reach extraction.
    var file = form.Files.FirstOrDefault();
    if (file is null)
    {
        return Results.Json(
            new ErrorResponse(
                "Invalid request",
                "No file was uploaded. Send multipart/form-data with a 'file' field.",
                StatusCodes.Status400BadRequest,
                context.Request.Path),
            JsonOptions.Default,
            statusCode: StatusCodes.Status400BadRequest);
    }

    if (file.Length == 0)
    {
        return Results.Json(
            new ErrorResponse(
                "Invalid request",
                $"'{file.FileName}' is empty. Upload a document with content.",
                StatusCodes.Status400BadRequest,
                context.Request.Path),
            JsonOptions.Default,
            statusCode: StatusCodes.Status400BadRequest);
    }

    var maxSize = extractionOptions.Value.MaxFileSizeBytes;
    if (file.Length > maxSize)
    {
        throw new FileTooLargeException(
            $"'{file.FileName}' is {file.Length:N0} bytes, above the {maxSize:N0} byte limit.");
    }

    await using var stream = file.OpenReadStream();

    var response = await pipeline.SummarizeFileAsync(
        stream,
        file.FileName,
        form["language"].ToString(),
        int.TryParse(form["maxWords"].ToString(), out var maxWords) ? maxWords : null,
        int.TryParse(form["chunkSize"].ToString(), out var chunkSize) ? chunkSize : null,
        cancellationToken);

    return Results.Ok(response);
})
.WithName("SummarizeFile")
.WithSummary("Summarize an uploaded document")
.WithTags("Summarize")
.DisableAntiforgery();

app.MapGet("/health", async (
        ISummarizationEngine engine,
        IOptions<SummarizationOptions> options,
        IOptions<ExtractionOptions> extractionOptions,
        CancellationToken cancellationToken) =>
{
    var summarization = options.Value;
    var reachable = await engine.IsHealthyAsync(cancellationToken);

    // 503 when Ollama cannot serve requests, so an orchestrator or uptime check can
    // act on the status code instead of parsing the body.
    return Results.Json(
        new
        {
            status = reachable ? "healthy" : "degraded",
            ollama = new
            {
                endpoint = summarization.OllamaEndpoint,
                reachable,
                model = summarization.Model,
                modelPresent = reachable,
                temperature = summarization.Temperature,
                numCtx = summarization.NumCtx,
                think = summarization.Think,
                timeoutSeconds = (int)summarization.Timeout.TotalSeconds,
            },
            supportedFormats = SupportedFormats.Extensions.OrderBy(e => e).ToArray(),
            maxFileSizeBytes = extractionOptions.Value.MaxFileSizeBytes,
        },
        statusCode: reachable ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
})
.WithName("Health")
.WithSummary("Service, Ollama and model status")
.WithTags("Health");

app.Run();

/// <summary>
/// Raised when an upload exceeds the configured size limit. Mapped to 413 rather
/// than 422 so a client can distinguish "too big" from "unreadable".
/// </summary>
public sealed class FileTooLargeException : DocumentPipelineException
{
    public FileTooLargeException(string message) : base(message)
    {
    }
}
