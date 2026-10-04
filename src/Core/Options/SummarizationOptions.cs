namespace Core.Options;

/// <summary>
/// Inference settings for the local model. Defaults are pinned to values that suit
/// a 1.7B model on a CPU-only host; every one is overridable from configuration.
/// </summary>
public sealed class SummarizationOptions
{
    public const string SectionName = "Summarization";

    /// <summary>Model id passed to Ollama.</summary>
    public string Model { get; set; } = "qwen3:1.7b-q4_K_M";

    /// <summary>
    /// Base address of the Ollama server. Native Windows development uses
    /// http://localhost:11434; docker-compose overrides this to http://ollama:11434.
    /// </summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Low temperature: summarization should copy the source rather than sample.
    /// Higher values risk garbling figures and names.
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>
    /// Context window. Sized to comfortably hold a 2400-token chunk plus the
    /// system prompt and a generous output allowance.
    /// </summary>
    public int NumCtx { get; set; } = 8192;

    /// <summary>
    /// Reasoning tokens are disabled. They add latency without improving
    /// summarization fidelity at this model size.
    /// </summary>
    public bool Think { get; set; }

    /// <summary>Upper bound on generated tokens per model call.</summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>
    /// Per-call timeout. A 1.7B model on a 4-core CPU genuinely takes minutes;
    /// a conventional HTTP timeout would produce spurious failures.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Target length of the final summary, in words.</summary>
    public int MaxSummaryWords { get; set; } = 300;
}