using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Util;

namespace Infrastructure.Extraction;

/// <summary>
/// Reads PDFs with PdfPig. Works page by page so the result can report where content
/// came from, and so a document mixing digital and scanned pages is described
/// honestly rather than appearing uniformly empty.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;
    private readonly ExtractionOptions _options;

    /// <summary>
    /// NearestNeighbourWordExtractor groups letters into words and orders them into
    /// lines, which recovers reading order from the content stream. It is used in
    /// preference to <c>DefaultWordExtractor</c>, whose constructor is not public in
    /// this version, and because its ordering is markedly better on multi-column pages.
    /// </summary>
    private static readonly IWordExtractor WordExtractor = new NearestNeighbourWordExtractor();

    public PdfTextExtractor(
        ILogger<PdfTextExtractor> logger,
        ExtractionOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public bool Supports(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractionResult> ExtractAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // PdfPig requires a seekable stream; the upload stream may not be seekable.
        var seekable = new MemoryStream();
        stream.CopyTo(seekable);
        seekable.Position = 0;

        try
        {
            using var document = PdfDocument.Open(seekable);

            var pageTexts = new List<string>(document.NumberOfPages);
            var charsPerPage = new List<int>(document.NumberOfPages);
            var warnings = new List<string>();
            var unreadablePages = 0;

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = ReadPageText(page);
                pageTexts.Add(text);
                charsPerPage.Add(text.Length);

                if (text.Length < _options.MinimumCharsPerPage)
                {
                    unreadablePages++;
                }
            }

            var totalChars = charsPerPage.Sum();
            if (totalChars == 0)
            {
                throw new UnsupportedDocumentException(
                    $"'{fileName}' yielded no text layer across {document.NumberOfPages} page(s). "
                    + "This usually means it is a scanned or image-only PDF, which needs OCR. "
                    + "OCR is not supported in v1.");
            }

            if (unreadablePages > 0)
            {
                warnings.Add(
                    $"{unreadablePages} of {document.NumberOfPages} page(s) contained no usable "
                    + "text layer and were skipped; they may be scanned images.");
            }

            // Pages joined with a blank line so chunking sees them as separate blocks.
            var combined = TextNormalizer.Normalize(string.Join("\n\n", pageTexts));

            _logger.LogDebug(
                "Extracted {PageCount} page(s) ({CharCount} chars, {Unreadable} unreadable) from {FileName}",
                document.NumberOfPages,
                totalChars,
                unreadablePages,
                fileName);

            return Task.FromResult(ExtractionResult.FromDocument(
                new TextDocument(fileName, combined, charsPerPage),
                warnings));
        }
        catch (UnsupportedDocumentException)
        {
            throw;
        }
        catch (PdfDocumentFormatException ex)
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' is not a readable PDF.", ex);
        }
        finally
        {
            seekable.Dispose();
        }
    }

    /// <summary>
    /// Recovers text for one page.
    /// </summary>
    /// <remarks>
    /// PdfPig reconstructs whole words from the page letters but exposes no ready-made
    /// string, so the words are joined here. Line breaks are detected from the bounding
    /// boxes: a word sitting meaningfully below the previous one starts a new line.
    /// Joining with spaces alone would flatten the page into one run of text and
    /// destroy the paragraph structure the chunker depends on.
    /// </remarks>
    private static string ReadPageText(Page page)
    {
        var words = WordExtractor.GetWords(page.Letters).ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(
            words.Sum(w => w.Text.Length) + words.Count);

        for (var i = 0; i < words.Count; i++)
        {
            if (i > 0)
            {
                var previous = words[i - 1];
                var current = words[i];
                var descent = current.BoundingBox.Bottom - previous.BoundingBox.Bottom;

                builder.Append(descent > LineBreakThreshold(current) ? '\n' : ' ');
            }

            builder.Append(words[i].Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Fraction of a word's height that it must fall below the previous word to count
    /// as a new line. Large enough that kerning and baseline jitter do not invent
    /// line breaks, small enough to catch real ones.
    /// </summary>
    private const double LineBreakFactor = 0.5;

    private static double LineBreakThreshold(Word word)
    {
        var height = word.BoundingBox.Height;
        return height > 0 ? height * LineBreakFactor : 1d;
    }
}