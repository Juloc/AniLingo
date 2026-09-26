namespace AniLingo.Web.Features.Vocabulary;

/// <summary>
/// Lexical catalog entry extracted from subtitles and enriched from the bundled
/// dictionary. Terms are shared, profile-independent data; per-profile learning
/// state lives in Learning cards whose unit links back through LearningUnit.TermId.
/// </summary>
public sealed class Term
{
    /// <summary>
    /// Language of <see cref="Meaning"/>. The bundled JMdict lookup is German
    /// (with English glosses only where no German entry exists).
    /// </summary>
    public const string MeaningLanguage = "de";

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Language { get; set; } = "ja";
    public string Canonical { get; set; } = "";
    public string? Reading { get; set; }
    public string? Meaning { get; set; }
}

public sealed class EpisodeTerm
{
    public Guid EpisodeId { get; set; }
    public Guid TermId { get; set; }
    public int Occurrences { get; set; }
    public int FirstCueStartMs { get; set; }
}
