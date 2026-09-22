namespace AniLingo.Web.Features.Vocabulary;

public sealed class Term
{
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
