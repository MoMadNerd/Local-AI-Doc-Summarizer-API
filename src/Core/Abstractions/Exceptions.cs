namespace Core.Abstractions;

/// <summary>
/// Base for failures that the API layer translates into an HTTP response.
/// </summary>
public abstract class DocumentPipelineException : Exception
{
    protected DocumentPipelineException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The uploaded document carried no usable text. A scanned or image-only PDF in v1,
/// or an empty file. This is a distinct failure from "unsupported format": the file
/// was accepted but nothing could be read from it.
/// </summary>
public sealed class UnsupportedDocumentException : DocumentPipelineException
{
    public UnsupportedDocumentException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The summarization backend failed — unreachable, timed out, or returned nothing.
/// </summary>
public sealed class SummarizationException : DocumentPipelineException
{
    public SummarizationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}