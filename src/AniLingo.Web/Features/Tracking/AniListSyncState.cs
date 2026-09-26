using System.Text.Json;

namespace AniLingo.Web.Features.Tracking;

/// <summary>
/// Per-profile automatic AniList sync setting. Stored with the profile's
/// AniList connection in <see cref="AniListAccountStore"/>.
/// </summary>
public enum AniListSyncMode
{
    /// <summary>Manual only: AniList is written only when the user presses Sync.</summary>
    Off = 0,

    /// <summary>Write promptly after an episode is watched or a chapter/volume is finished.</summary>
    OnCompletion = 1,

    /// <summary>Write forward progress after watching/reading checkpoints once playback/reading pauses.</summary>
    Continuous = 2
}

public enum AniListSyncItemStatus
{
    /// <summary>AniList progress was increased by automatic sync.</summary>
    Synced,

    /// <summary>Nothing to write: AniList already matches or is ahead.</summary>
    UpToDate,

    /// <summary>A safety rule prevented the write; rechecked hourly, backing off to daily.</summary>
    Blocked,

    /// <summary>AniList failed or rate-limited the request; retried with exponential backoff.</summary>
    Failed,

    /// <summary>The local work has no AniList identity; local-only progress, never shown as activity.</summary>
    NotMatched
}

/// <summary>
/// Sync cursor and last outcome for one local work of one profile.
/// <see cref="HandledLocalUpdatedAt"/> and <see cref="HandledCompletionMarker"/>
/// describe the newest local checkpoint that was reconciled successfully; they
/// only advance after a successful write or a confirmed no-op.
/// <see cref="FailureCount"/> counts consecutive attempts without success.
/// </summary>
public sealed record AniListSyncItem(
    string MediaKind,
    Guid LocalId,
    string Title,
    AniListSyncItemStatus Status,
    string Message,
    DateTime? HandledLocalUpdatedAt,
    string? HandledCompletionMarker,
    int? LocalProgress,
    int? RemoteProgress,
    DateTimeOffset LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    int FailureCount,
    DateTimeOffset? NextAttemptAt);

/// <summary>Automatic sync state of one profile, bound to one AniList user.</summary>
public sealed record AniListSyncState(
    int ViewerId,
    IReadOnlyList<AniListSyncItem> Items)
{
    public static AniListSyncState Empty(int viewerId) => new(viewerId, []);

    public AniListSyncItem? Find(string mediaKind, Guid localId) =>
        Items.FirstOrDefault(x =>
            x.LocalId == localId &&
            string.Equals(x.MediaKind, mediaKind, StringComparison.Ordinal));
}

/// <summary>
/// Profile-private automatic sync cursors and activity, stored beside the
/// profile's AniList connection under /data/integrations/anilist/sync.
/// </summary>
public sealed class AniListSyncStateStore
{
    /// <summary>Upper bound of remembered works per profile; oldest attempts are dropped first.</summary>
    public const int MaxItemsPerProfile = 500;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly ILogger<AniListSyncStateStore> logger;
    private readonly string syncDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public AniListSyncStateStore(ILogger<AniListSyncStateStore> logger)
        : this(logger, new DirectoryInfo("/data/integrations"))
    {
    }

    public AniListSyncStateStore(
        ILogger<AniListSyncStateStore> logger,
        DirectoryInfo integrationDirectory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(integrationDirectory);

        this.logger = logger;
        syncDirectory = Path.Combine(
            integrationDirectory.FullName,
            "anilist",
            "sync");
    }

    /// <summary>
    /// Loads the profile's state for the given AniList user. State recorded for
    /// another AniList user is discarded so cursors never leak across accounts.
    /// </summary>
    public async Task<AniListSyncState> LoadAsync(
        string profileId,
        int viewerId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return AniListSyncState.Empty(viewerId);
            }

            var state = JsonSerializer.Deserialize<AniListSyncState>(
                await File.ReadAllTextAsync(path, cancellationToken),
                JsonOptions);

            return state is null || state.ViewerId != viewerId
                ? AniListSyncState.Empty(viewerId)
                : state with { Items = state.Items ?? [] };
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Could not read AniList sync state for profile {ProfileId}; starting fresh.",
                profileId);
            return AniListSyncState.Empty(viewerId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        string profileId,
        AniListSyncState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = GetPath(profileId);
        var bounded = state.Items.Count <= MaxItemsPerProfile
            ? state
            : state with
            {
                Items = state.Items
                    .OrderByDescending(x => x.LastAttemptAt)
                    .Take(MaxItemsPerProfile)
                    .ToArray()
            };

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(syncDirectory);
            var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(bounded, JsonOptions),
                    cancellationToken);
                if (OperatingSystem.IsLinux())
                {
                    File.SetUnixFileMode(
                        temporaryPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(string profileId) =>
        Path.Combine(
            syncDirectory,
            $"{AniListAccountStore.ValidateProfileId(profileId)}.json");
}
