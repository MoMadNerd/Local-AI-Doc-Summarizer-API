namespace Core.Entities;

/// <summary>
/// Result of turning an uploaded file into text. Carries warnings so that lossy
/// extraction (an unreadable page, an empty OCR-free scanned PDF) is reported to
/// the caller instead of being silently absorbed into a summary.
/// </summary>
/// <param name="Document">The recovered text.</param>
/// <param name="Warnings">
/// Non-fatal problems encountered during extraction, in the order they occurred.
/// </param>
public sealed record ExtractionResult(
    TextDocument Document,
    IReadOnlyList<string> Warnings)
{
    public static ExtractionResult FromDocument(
        TextDocument document,
        IReadOnlyList<string>? warnings = null) =>
        new(document, warnings ?? Array.Empty<string>());
}