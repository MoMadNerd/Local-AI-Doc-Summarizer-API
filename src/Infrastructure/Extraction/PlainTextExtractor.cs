using System.Text;
using Core.Abstractions;
using Core.Entities;
using Core.Options;

namespace Infrastructure.Extraction;

/// <summary>
/// Reads TXT and Markdown files. Markdown syntax is stripped so the summarizer
/// sees prose rather than markup.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public bool Supports(string fileName) =>
        SupportedFormats.IsSupported(fileName)
        && !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<ExtractionResult> ExtractAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadAllBytesAsync(stream, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' is empty, so there is nothing to summarize.");
        }

        var text = Decode(bytes);
        var isMarkdown = IsMarkdown(fileName);
        if (isMarkdown)
        {
            text = MarkdownText.Strip(text);
        }

        text = TextNormalizer.Normalize(text);

        if (text.Length == 0)
        {
            throw new UnsupportedDocumentException(
                $"'{fileName}' contained no readable text after normalization.");
        }

        return ExtractionResult.FromDocument(
            new TextDocument(fileName, text, Array.Empty<int>()));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, 81920, ct);
        return memory.ToArray();
    }

    /// <summary>
    /// Decodes as UTF-8, falling back to Latin-1. A UTF-8 BOM is stripped by the
    /// decoder; a strict UTF-8 failure on legacy Western-European files is common
    /// enough to be worth handling rather than surfacing as mojibake.
    /// </summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static bool IsMarkdown(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
    }
}