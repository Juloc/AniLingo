using System.Text.Json;

namespace Jularr.Web.Features.OfflineLibrary;

/// <summary>
/// Per-profile smart offline prefetch policy (#415). The server owns the
/// policy and the selection; the native client only executes the resulting
/// plan through the existing offline endpoints. Off by default: nothing is ever
/// prefetched until the profile turns it on.
/// </summary>
public sealed record OfflinePrefetchPolicy
{
    private const long MiB = 1024L * 1024L;
    private const long GiB = 1024L * MiB;

    public const long MinCapBytes = 256 * MiB;
    public const long MaxCapBytes = 500 * GiB;
    public const long DefaultCapBytes = 5 * GiB;
    public const int MaxEpisodesAhead = 5;
    public const int MaxChaptersAhead = 10;

    /// <summary>Cap presets offered by the settings page; any value in the valid range is accepted.</summary>
    public static IReadOnlyList<long> CapChoices { get; } =
        [1 * GiB, 2 * GiB, 5 * GiB, 10 * GiB, 20 * GiB, 50 * GiB, 100 * GiB];

    public static OfflinePrefetchPolicy Default { get; } = new();

    /// <summary>Master switch. Prefetch is Off until a profile enables it.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Hard budget for prefetched content on one device. Only prefetched items
    /// count against it: explicit user downloads are never counted, evicted or
    /// limited by this cap.
    /// </summary>
    public long CapBytes { get; init; } = DefaultCapBytes;

    /// <summary>Prefetch next unwatched episodes (continue-series).</summary>
    public bool IncludeEpisodes { get; init; } = true;

    /// <summary>Prefetch next unread chapters of Books and Novels.</summary>
    public bool IncludeChapters { get; init; } = true;

    /// <summary>How many unwatched episodes per series are kept ready, starting at the one to continue with.</summary>
    public int EpisodesAhead { get; init; } = 2;

    /// <summary>How many unread chapters per work are kept ready, starting at the one to continue with.</summary>
    public int ChaptersAhead { get; init; } = 3;

    /// <summary>Whether prefetching may download over a metered connection. Off by default.</summary>
    public bool AllowMetered { get; init; }

    /// <summary>Throws <see cref="InvalidOperationException"/> when a value is out of range.</summary>
    public OfflinePrefetchPolicy Validated()
    {
        if (CapBytes is < MinCapBytes or > MaxCapBytes)
        {
            throw new InvalidOperationException(
                $"The prefetch storage cap must be between {MinCapBytes / MiB} MB and {MaxCapBytes / GiB} GB.");
        }

        if (EpisodesAhead is < 1 or > MaxEpisodesAhead)
        {
            throw new InvalidOperationException(
                $"Episodes ahead must be between 1 and {MaxEpisodesAhead}.");
        }

        if (ChaptersAhead is < 1 or > MaxChaptersAhead)
        {
            throw new InvalidOperationException(
                $"Chapters ahead must be between 1 and {MaxChaptersAhead}.");
        }

        if (Enabled && !IncludeEpisodes && !IncludeChapters)
        {
            throw new InvalidOperationException("Choose at least one kind of content to prefetch.");
        }

        return this;
    }
}

/// <summary>
/// JSON settings store (<c>{dataRoot}/offline/prefetch/{profileId}.json</c>),
/// one file per profile. A missing or unreadable file means the safe default:
/// prefetch Off.
/// </summary>
public sealed class OfflinePrefetchPolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string directory;
    private readonly ILogger<OfflinePrefetchPolicyStore>? logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    public OfflinePrefetchPolicyStore(string dataRoot, ILogger<OfflinePrefetchPolicyStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        directory = Path.Combine(dataRoot, "offline", "prefetch");
        this.logger = logger;
    }

    public async Task<OfflinePrefetchPolicy> LoadAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return OfflinePrefetchPolicy.Default;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                var policy = JsonSerializer.Deserialize<OfflinePrefetchPolicy>(json, JsonOptions);
                return policy?.Validated() ?? OfflinePrefetchPolicy.Default;
            }
            catch (Exception exception) when (
                exception is JsonException or InvalidOperationException)
            {
                // A damaged or out-of-range file must never switch prefetching on.
                logger?.LogWarning(
                    exception,
                    "Offline prefetch policy of profile {ProfileId} is invalid; prefetch stays off.",
                    profileId);
                return OfflinePrefetchPolicy.Default;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        string profileId,
        OfflinePrefetchPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var path = PathFor(profileId);
        var validated = policy.Validated();

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(validated, JsonOptions),
                    cancellationToken);
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

    private string PathFor(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (profileId.Length > 80 ||
            profileId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Profile ID contains unsupported characters.", nameof(profileId));
        }

        return Path.Combine(directory, $"{profileId}.json");
    }
}
