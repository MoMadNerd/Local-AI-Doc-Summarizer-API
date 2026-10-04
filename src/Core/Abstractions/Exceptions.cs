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

    /// <summary>
    /// Stable machine-readable identifier for this failure, surfaced in the error
    /// payload so a client can branch on the code instead of matching on prose.
    /// Null when the failure has no distinct code of its own.
    /// </summary>
    public virtual string? ErrorCode => null;
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
/// The caller sent less text than the configured summarizable floor. Distinct from
/// <see cref="UnsupportedDocumentException"/>: the input was readable, there was just
/// not enough of it to summarize, and the model was never called.
/// </summary>
public sealed class InputTooShortException : DocumentPipelineException
{
    /// <summary>Stable error code reported to clients.</summary>
    public const string Code = "input_too_short";

    public InputTooShortException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public override string? ErrorCode => Code;
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