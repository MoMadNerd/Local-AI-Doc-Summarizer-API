using System.ComponentModel.DataAnnotations;

namespace Core.Dtos;

/// <summary>
/// Request body for the raw-text summarize endpoint. For file upload see
/// <c>POST /api/v1/summarize</c>.
/// </summary>
public sealed record SummarizeTextRequest
{
    /// <summary>Text to summarize.</summary>
    [Required]
    [MinLength(1)]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Target language: <c>auto</c> to detect from the text, or a forced code.
    /// </summary>
    [RegularExpression("^(auto|ar|en)$")]
    public string Language { get; init; } = "auto";

    /// <summary>
    /// Target summary length in words. Optional; the configured default applies.
    /// </summary>
    [Range(20, 2000)]
    public int? MaxWords { get; init; }

    /// <summary>
    /// Chunk budget override, in tokens. Optional; the configured default applies.
    /// </summary>
    [Range(256, 8192)]
    public int? ChunkSize { get; init; }
}