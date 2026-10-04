using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Infrastructure.Ollama;

/// <summary>
/// Summarizes text through a local Ollama server.
/// </summary>
/// <remarks>
/// In OllamaSharp 5.5.0 <see cref="OllamaApiClient"/> implements
/// <see cref="IChatClient"/> directly, so it is used through that interface: the
/// pipeline can then be exercised in tests against a fake IChatClient with no model
/// running.
/// </remarks>
public sealed class OllamaSummarizationEngine : ISummarizationEngine, IDisposable
{
    private readonly ILogger<OllamaSummarizationEngine> _logger;
    private readonly SummarizationOptions _options;
    private readonly IChatClient _chatClient;
    private readonly OllamaApiClient _client;
    private bool _disposed;

    public OllamaSummarizationEngine(
        ILogger<OllamaSummarizationEngine> logger,
        SummarizationOptions options)
    {
        _logger = logger;
        _options = options;
        _client = new OllamaApiClient(_options.OllamaEndpoint);
        _chatClient = _client;
    }

    /// <summary>
    /// The system prompt stays in English even when the summary must be Arabic:
    /// small instruct models follow an English instruction more reliably than the
    /// same instruction in their weaker language, and the output language is stated
    /// explicitly.
    /// </summary>
    private static string BuildSystemPrompt(LanguageCode language, int maxWords)
    {
        var outputLanguage = language switch
        {
            LanguageCode.Arabic =>
                "Write the summary in Arabic, using Modern Standard Arabic.",
            LanguageCode.English =>
                "Write the summary in English.",
            _ =>
                "Write the summary in the same language as the source text.",
        };

        return $"""
            You are a precise document summarizer.

            Rules you must follow:
            - {outputLanguage}
            - Summarize only what the text actually says. Never add facts.
            - Never invent numbers, dates, names, or figures. If a figure is absent, leave it out.
            - Keep names, technical terms and identifiers exactly as written.
            - Preserve the original order of events and arguments.
            - Do not use Markdown, bullet points, headings, or code fences.
            - Do not open with phrases such as "This document discusses".
            - Aim for roughly {maxWords} words. Be shorter for short inputs.
            """;
    }

    public async Task<string> SummarizeAsync(
        string text,
        LanguageCode language,
        SummarizationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var settings = options ?? _options;

        // Reasoning tokens add latency without improving summaries at this model size,
        // so they are switched off explicitly rather than left to the server default.
        var chatOptions = new ChatOptions
        {
            ModelId = settings.Model,
            Temperature = (float)settings.Temperature,
            MaxOutputTokens = settings.MaxOutputTokens,

            // NumCtx has no equivalent on ChatOptions, so it rides along as an Ollama
            // option. Without it the server default of 4096 would truncate a
            // 2400-token chunk once prompt and output are added.
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["num_ctx"] = settings.NumCtx,
            },
        };

        // ChatRole is ambiguous between Microsoft.Extensions.AI and OllamaSharp; the
        // Microsoft.Extensions.AI one is correct for the IChatClient surface.
        var messages = new List<ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System,
                BuildSystemPrompt(language, settings.MaxSummaryWords)),
            new(Microsoft.Extensions.AI.ChatRole.User, text),
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(settings.Timeout);

        try
        {
            // SelectedModel travels on the shared client, so it is set per call to keep
            // the model id in one place; the engine is a singleton and is not re-entrant
            // across differing models.
            _client.SelectedModel = settings.Model;

            var response = await _chatClient.GetResponseAsync(messages, chatOptions, timeoutCts.Token);

            var summary = response?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new SummarizationException(
                    $"Model '{settings.Model}' returned an empty summary.");
            }

            _logger.LogDebug(
                "Summarized {InputChars} chars into {OutputChars} chars using {Model}",
                text.Length,
                summary.Length,
                settings.Model);

            return summary;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SummarizationException(
                $"Summarization timed out after {settings.Timeout.TotalMinutes:0} minutes. "
                + "A small model on a CPU-only host is slow; raise Summarization:Timeout if "
                + "this is expected.");
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled: propagate rather than misreport as a timeout.
            throw;
        }
        catch (SummarizationException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cannot reach Ollama at {Endpoint}", settings.OllamaEndpoint);
            throw new SummarizationException(
                $"Could not reach the Ollama server at {settings.OllamaEndpoint}. "
                + "Confirm Ollama is running and the model is pulled.",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Summarization failed against {Endpoint}", settings.OllamaEndpoint);
            throw new SummarizationException(
                $"Summarization failed against {settings.OllamaEndpoint}: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(cancellationToken);

            foreach (var model in models)
            {
                // Ollama reports names such as "qwen3:1.7b-q4_K_M:latest".
                if (model.Name.StartsWith(_options.Model, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            _logger.LogWarning(
                "Ollama is reachable but model '{Model}' is not present locally", _options.Model);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health probe failed at {Endpoint}", _options.OllamaEndpoint);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }
}
