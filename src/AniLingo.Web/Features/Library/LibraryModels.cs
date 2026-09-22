namespace AniLingo.Web.Features.Library;

public sealed class LibraryRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastScannedAt { get; set; }
}

public sealed class Anime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public int SeasonNumber { get; set; } = 1;
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LibraryRootId { get; set; }
    public Guid EpisodeId { get; set; }
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset LastWriteTimeUtc { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";
    public string? BootstrapRoot { get; set; }
}

public sealed record ScanResult(int Discovered, int Updated, int Skipped, int SubtitleFiles);
