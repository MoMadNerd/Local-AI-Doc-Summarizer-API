namespace Core.Options;

/// <summary>
/// Upload limits and extraction behaviour.
/// </summary>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>Maximum accepted upload size in bytes.</summary>
    public long MaxFileSizeBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>
    /// Characters extracted from a page below which the page is treated as
    /// carrying no usable text layer.
    /// </summary>
    public int MinimumCharsPerPage { get; set; } = 20;

    /// <summary>
    /// Always false in v1. OCR for scanned and image-only documents is roadmap
    /// work; the flag exists so enabling it later is a configuration change rather
    /// than a code change.
    /// </summary>
    public bool EnableOcr { get; set; }
}