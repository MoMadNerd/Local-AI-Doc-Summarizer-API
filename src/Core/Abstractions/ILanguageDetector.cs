using Core.Entities;

namespace Core.Abstractions;

/// <summary>
/// Determines which language a document is written in, so the summarizer can be
/// prompted to answer in kind. Implemented deterministically by counting script
/// characters — no model call, and no tokens spent asking.
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Classifies the language of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Document text. Only a leading sample is inspected.</param>
    /// <returns>
    /// The detected language, or <see cref="LanguageCode.Unknown"/> when the text
    /// is too short or too evenly mixed to call confidently.
    /// </returns>
    LanguageCode Detect(string text);
}