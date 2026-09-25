namespace AniLingo.Web.Features.Library;

public sealed class LibraryRoot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastScannedAt { get; set; }
    public bool WakeOnLanEnabled { get; set; }
    public string? WakeMacAddress { get; set; }
    public string? WakeBroadcastAddress { get; set; }
}

public sealed class Anime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Episode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnimeId { get; set; }
    public int SeasonNumber { get; set; } = 1;
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

public sealed class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LibraryRootId { get; set; }
    public Guid EpisodeId { get; set; }
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";
    public string? BootstrapRoot { get; set; }
}

public sealed record ScanResult(int Discovered, int Updated, int Skipped, int SubtitleFiles)
{
    public int Removed { get; init; }

    // NFO files that were present but rejected (malformed, oversized, unreadable or unsupported).
    public int MetadataWarnings { get; init; }
}
