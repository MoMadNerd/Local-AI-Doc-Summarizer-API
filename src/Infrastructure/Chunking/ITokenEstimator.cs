namespace Infrastructure.Chunking;

/// <summary>
/// Estimates how many tokens a piece of text occupies. An estimate rather than an
/// exact count: a real tokenizer would add a dependency to the chunking path for
/// marginal accuracy, and the budget only needs to stay on the safe side.
/// </summary>
public interface ITokenEstimator
{
    int Estimate(string text);
}

/// <summary>
/// Approximates token count from character length. Around four characters per token
/// holds for English prose and runs slightly low for Arabic, which errs toward
/// smaller chunks — the safe direction, since an oversized chunk risks truncation.
/// </summary>
public sealed class HeuristicTokenEstimator : ITokenEstimator
{
    private const double CharactersPerToken = 4d;

    public int Estimate(string text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : Math.Max(1, (int)Math.Ceiling(text.Length / CharactersPerToken));
}