namespace Core.Options;

/// <summary>
/// File extensions the v1 API accepts. Extension-based dispatch only — the
/// extractors inspect content, never a client-supplied MIME type.
/// </summary>
public static class SupportedFormats
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(
        new[] { ".pdf", ".docx", ".txt", ".md", ".markdown" },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);
    }
}