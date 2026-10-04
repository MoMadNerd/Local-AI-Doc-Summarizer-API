using Core.Entities;

namespace Core.Abstractions;

/// <summary>
/// Turns one family of source documents into plain text.
/// One implementation per format, resolved through DI by
/// <see cref="Supports"/>.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Whether this extractor handles the given file name, decided on extension.
    /// </summary>
    bool Supports(string fileName);

    /// <summary>
    /// Reads a document and returns its text.
    /// </summary>
    /// <param name="stream">Source bytes. The caller owns disposal.</param>
    /// <param name="fileName">Original file name, used for format dispatch and diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnsupportedDocumentException">
    /// The file carries no usable text — a scanned or image-only PDF, for example.
    /// </exception>
    Task<ExtractionResult> ExtractAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default);
}