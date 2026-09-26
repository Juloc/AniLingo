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

    // Periodic safety reconciliation of this root; 0 disables it.
    public int ReconciliationIntervalMinutes { get; set; } = DefaultReconciliationIntervalMinutes;

    public const int DefaultReconciliationIntervalMinutes = 30;
    public const int MinimumReconciliationIntervalMinutes = 5;
    public const int MaximumReconciliationIntervalMinutes = 24 * 60;
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

    // Media files enumerated in the scanned scope.
    public int MediaFiles { get; init; }

    public int ArtworkImported { get; init; }

    // Per-item failures that did not stop the scan (for example provider matching).
    public int Errors { get; init; }

    // Item-level warnings with root-relative paths; bounded to MaxRecordedWarnings.
    public IReadOnlyList<ScanWarning> Warnings { get; init; } = [];

    // Total number of warnings including those beyond the recorded bound.
    public int WarningCount { get; init; }

    public const int MaxRecordedWarnings = 200;
}

// A root-relative path only: scan diagnostics never carry the host path of the root.
public sealed record ScanWarning(string Reason, string RelativePath);

public enum LibraryScanPhase
{
    Queued = 0,
    Enumerating = 1,
    Reconciling = 2,
    Metadata = 3,
    Artwork = 4,
    Subtitles = 5,
    Completed = 6
}

public sealed record LibraryScanProgress(LibraryScanPhase Phase, int Processed, int Total);

public delegate Task LibraryScanProgressHandler(
    LibraryScanProgress progress,
    CancellationToken cancellationToken);
