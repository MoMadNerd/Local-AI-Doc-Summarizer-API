using System.Text.Json;
using Core.Abstractions;
using Core.Dtos;

namespace Api.Middleware;

/// <summary>
/// Translates pipeline failures into HTTP responses.
/// </summary>
/// <remarks>
/// Status codes are chosen so a caller can tell the failures apart without reading
/// the message: 400 is a bad request, 413 a file too large, 422 a document we
/// accepted but could not read, 502 a failure in the model backend, and 504 a
/// backend timeout.
/// </remarks>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody left to send a response to.
            _logger.LogInformation("Request aborted by the client: {Path}", context.Request.Path);
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(context, ex);
        }
    }

    private async Task WriteErrorAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            FileTooLargeException tooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "File too large", tooLarge.Message),

            UnsupportedDocumentException unsupported =>
                (StatusCodes.Status422UnprocessableEntity, "Unsupported document", unsupported.Message),

            SummarizationException summarization =>
                (StatusCodes.Status502BadGateway, "Summarization failed", summarization.Message),

            // Kestrel raises BadHttpRequestException with StatusCode 413 when the
            // body exceeds MaxRequestBodySize, before any handler runs.
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } tooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "File too large", tooLarge.Message),

            BadHttpRequestException bad =>
                (StatusCodes.Status400BadRequest, "Invalid request", bad.Message),

            ArgumentException argument =>
                (StatusCodes.Status400BadRequest, "Invalid request", argument.Message),

            KeyNotFoundException missing =>
                (StatusCodes.Status400BadRequest, "Invalid request", missing.Message),

            OperationCanceledException =>
                (StatusCodes.Status504GatewayTimeout, "Summarization timed out",
                    "The request exceeded the configured timeout."),

            _ =>
                (StatusCodes.Status500InternalServerError, "Unexpected error",
                    "An unexpected error occurred while processing the request."),
        };

        if (status >= 500)
        {
            _logger.LogError(exception, "{Title} on {Path}", title, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Status} {Title} on {Path}: {Detail}",
                status, title, context.Request.Path, detail);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; cannot write error for {Path}",
                context.Request.Path);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ErrorResponse(
            title,
            detail,
            status,
            context.Request.Path);

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions.Default));
    }
}

/// <summary>Consistent error envelope for every failure path.</summary>
public sealed record ErrorResponse(
    string Title,
    string Detail,
    int Status,
    string Path);

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}