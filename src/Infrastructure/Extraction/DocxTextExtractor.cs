using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Core.Abstractions;
using Core.Entities;
using Core.Options;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Extraction;

/// <summary>
/// Reads DOCX by opening the package and walking <c>word/document.xml</c> directly.
/// Parsing the XML ourselves avoids a third-party dependency for what is a flat,
/// well-documented format: paragraphs are <c>w:p</c> elements and their text lives
/// in <c>w:t</c> runs.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    private const string DocumentEntryPath = "word/document.xml";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly ILogger<DocxTextExtractor> _logger;

    public DocxTextExtractor(ILogger<DocxTextExtractor> logger) => _logger = logger;

    public bool Supports(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<ExtractionResult> ExtractAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // ZipArchive requires a seekable stream and the upload may not be seekable,
        // so buffer before opening the archive.
        using var seekable = new MemoryStream();
        await stream.CopyToAsync(seekable, 81920, cancellationToken);
        seekable.Position = 0;

        try
        {
            using var archive = new ZipArchive(seekable, ZipArchiveMode.Read, leaveOpen: true);

            var entry = archive.GetEntry(DocumentEntryPath);
            if (entry is null)
            {
                throw new UnsupportedDocumentException(
                    $"'{fileName}' is not a valid DOCX package: '{DocumentEntryPath}' is missing.");
            }

            using var entryStream = entry.Open();
            var document = XDocument.Load(entryStream);

            return BuildResult(document, fileName, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            // ZipArchive throws this when a non-ZIP payload carries a .docx extension.
            throw new UnsupportedDocumentException(
                $"'{fileName}' could not be opened as a DOCX package.", ex);
        }
        catch (XmlException ex)
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' contains malformed document XML.", ex);
        }
    }

    private ExtractionResult BuildResult(
        XDocument document,
        string fileName,
        CancellationToken cancellationToken)
    {
        var body = document.Root?.Element(W + "body");
        if (body is null)
        {
            throw new UnsupportedDocumentException($"'{fileName}' contains no document body.");
        }

        var paragraphs = new List<string>();
        foreach (var paragraph in body.Descendants(W + "p"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ReadParagraphText(paragraph);
            if (text.Length > 0)
            {
                paragraphs.Add(text);
            }
        }

        var joined = TextNormalizer.Normalize(string.Join("\n\n", paragraphs));

        if (joined.Length == 0)
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' contains no readable text. If it holds only images or "
                + "embedded objects, OCR support would be required.");
        }

        _logger.LogDebug(
            "Extracted {ParagraphCount} paragraphs ({CharCount} chars) from {FileName}",
            paragraphs.Count,
            joined.Length,
            fileName);

        // DOCX exposes no stable page boundaries without a layout renderer, so
        // per-page counts are reported only for paginated formats such as PDF.
        return ExtractionResult.FromDocument(
            new TextDocument(fileName, joined, Array.Empty<int>()));
    }

    /// <summary>
    /// Concatenates the text of one paragraph. Tabs and explicit line breaks become
    /// spaces so words split across separate runs are not fused together.
    /// </summary>
    private static string ReadParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();

        foreach (var element in paragraph.Descendants())
        {
            if (element.Name == W + "t")
            {
                builder.Append(element.Value);
            }
            else if (element.Name == W + "br" || element.Name == W + "cr" || element.Name == W + "tab")
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }
}