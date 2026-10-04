namespace Core.Entities;

/// <summary>
/// Plain text recovered from a source document, along with the per-page character
/// counts that let the API report where content came from.
/// </summary>
/// <param name="Name">Original file name, retained for diagnostics.</param>
/// <param name="Text">Full extracted text. Never null; may be empty.</param>
/// <param name="CharsPerPage">
/// Character count per source page, in page order. Empty for formats without
/// pages (TXT, Markdown).
/// </param>
public sealed record TextDocument(
    string Name,
    string Text,
    IReadOnlyList<int> CharsPerPage)
{
    /// <summary>Total characters available to summarize.</summary>
    public int CharacterCount => Text.Length;
}