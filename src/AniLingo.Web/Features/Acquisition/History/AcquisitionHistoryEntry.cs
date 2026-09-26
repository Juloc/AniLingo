namespace AniLingo.Web.Features.Acquisition.History;

/// <summary>What happened to a release for one episode. Grabbed/Delayed come from the search seam,
/// Imported/Upgraded/ImportFailed/Dismissed from the import seam.</summary>
public enum AcquisitionHistoryEventKind
{
    Grabbed,
    Delayed,
    Imported,
    Upgraded,
    ImportFailed,
    Dismissed
}

/// <summary>
/// One canonical, append-only per-episode acquisition event (P1 item 6: richer upgrade
/// history). Kept in the database (not a JSON store) because it is a growing, queryable log rather
/// than current settings/state, and joins naturally to Anime by AnimeId. Nothing here is anime-
/// keyed by string key, so a series-folder rename (which only changes Anime.Key) never needs to
/// rewrite these rows.
/// </summary>
public sealed class AcquisitionHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }
    public AcquisitionHistoryEventKind EventKind { get; set; }
    public string? ReleaseTitle { get; set; }
    public string? ReleaseKey { get; set; }
    public int? Score { get; set; }
    public string? QualityKey { get; set; }
    public string? Indexer { get; set; }
    public string Reason { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
