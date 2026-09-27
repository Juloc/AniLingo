namespace AniLingo.Web.Features.Library;

// NFO-derived local metadata for an anime, read from its tvshow.nfo. This is the one canonical
// place for facts a local NFO file supplies that AnimeMetadata (Features/Metadata) cannot own,
// because AnimeMetadata belongs to a matched provider (AniList) and a fake "nfo" provider row
// would block automatic matching. NFO stays an input source, never a second state store: it does
// not replace or duplicate AnimeMetadata, and it is never treated as if it came from a provider.
//
// Display precedence: manual (an explicit user edit, if Jularr ever adds one) > provider
// metadata (AnimeMetadata) > this local NFO fallback > folder-derived title/no value.
public sealed class AnimeLocalMetadata
{
    public Guid AnimeId { get; set; }

    // Always "nfo" today; kept explicit so a future local source cannot silently share this row.
    public string Source { get; set; } = SourceNfo;

    public string? OriginalTitle { get; set; }
    public string? Plot { get; set; }
    public int? Year { get; set; }
    public DateOnly? Premiered { get; set; }

    // Additional provider IDs the NFO carried; AniList is matched through AnimeMetadata instead
    // and is intentionally not duplicated here.
    public string? MyAnimeListId { get; set; }
    public string? TvdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }

    // The source tvshow.nfo file's size and last-write time, used to skip re-parsing an
    // unchanged file on the next scan.
    public long SourceFileSizeBytes { get; set; }
    public DateTime SourceFileLastWriteTimeUtc { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const string SourceNfo = "nfo";
}
