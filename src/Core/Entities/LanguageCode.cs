namespace Core.Entities;

/// <summary>
/// Languages the summarizer can target. The source language is detected from the
/// document itself; it is never asked of the user unless they override it.
/// </summary>
public enum LanguageCode
{
    /// <summary>Detection was inconclusive, or the caller forced a neutral prompt.</summary>
    Unknown = 0,

    /// <summary>Arabic script.</summary>
    Arabic = 1,

    /// <summary>Latin script.</summary>
    English = 2,
}