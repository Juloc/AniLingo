using System.Text.Json;

namespace AniLingo.Web.Features.MediaMapping;

public sealed record MediaMappingReviewCandidate(
    string Provider,
    string ExternalId,
    string Title,
    int Score,
    IReadOnlyList<string> Evidence,
    string? Format = null,
    int? Year = null,
    int? UnitCount = null);

public sealed record MediaMappingReviewTask(
    Guid Id,
    string MediaType,
    string LocalId,
    string LocalTitle,
    string Purpose,
    string Reason,
    IReadOnlyList<MediaMappingReviewCandidate> Candidates,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class MediaMappingReviewStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly ILogger<MediaMappingReviewStore> logger;
    private readonly string storePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public MediaMappingReviewStore(
        ILogger<MediaMappingReviewStore> logger)
        : this(
            logger,
            new DirectoryInfo("/data/integrations/anilist"))
    {
    }

    public MediaMappingReviewStore(
        ILogger<MediaMappingReviewStore> logger,
        DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(directory);

        this.logger = logger;
        storePath = Path.Combine(
            directory.FullName,
            "mapping-review.json");
    }

    public async Task<IReadOnlyList<MediaMappingReviewTask>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadUnsafeAsync(cancellationToken))
                .OrderByDescending(x => x.UpdatedAt)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MediaMappingReviewTask?> FindPendingAsync(
        string mediaType,
        string localId,
        IReadOnlyCollection<string>? purposes = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedMediaType = Required(mediaType, nameof(mediaType));
        var normalizedLocalId = Required(localId, nameof(localId));
        var wantedPurposes = purposes is { Count: > 0 }
            ? purposes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadUnsafeAsync(cancellationToken))
                .Where(x =>
                    string.Equals(
                        x.MediaType,
                        normalizedMediaType,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        x.LocalId,
                        normalizedLocalId,
                        StringComparison.Ordinal) &&
                    (wantedPurposes is null ||
                     wantedPurposes.Contains(x.Purpose)))
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefault();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MediaMappingReviewTask> UpsertAsync(
        string mediaType,
        string localId,
        string localTitle,
        string purpose,
        string reason,
        IReadOnlyList<MediaMappingReviewCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var normalizedMediaType = Required(mediaType, nameof(mediaType));
        var normalizedLocalId = Required(localId, nameof(localId));
        var normalizedLocalTitle = Required(localTitle, nameof(localTitle));
        var normalizedPurpose = Required(purpose, nameof(purpose));
        var normalizedReason = Required(reason, nameof(reason));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadUnsafeAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var index = tasks.FindIndex(x =>
                string.Equals(
                    x.MediaType,
                    normalizedMediaType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.LocalId,
                    normalizedLocalId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.Purpose,
                    normalizedPurpose,
                    StringComparison.OrdinalIgnoreCase));

            MediaMappingReviewTask task;
            if (index >= 0)
            {
                var current = tasks[index];
                task = current with
                {
                    LocalTitle = normalizedLocalTitle,
                    Reason = normalizedReason,
                    Candidates = candidates.ToArray(),
                    UpdatedAt = now
                };
                tasks[index] = task;
            }
            else
            {
                task = new MediaMappingReviewTask(
                    Guid.NewGuid(),
                    normalizedMediaType,
                    normalizedLocalId,
                    normalizedLocalTitle,
                    normalizedPurpose,
                    normalizedReason,
                    candidates.ToArray(),
                    now,
                    now);
                tasks.Add(task);
            }

            await WriteUnsafeAsync(tasks, cancellationToken);
            return task;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ResolveAsync(
        string mediaType,
        string localId,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadUnsafeAsync(cancellationToken);
            var removed = tasks.RemoveAll(x =>
                string.Equals(
                    x.MediaType,
                    mediaType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.LocalId,
                    localId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.Purpose,
                    purpose,
                    StringComparison.OrdinalIgnoreCase)) > 0;

            if (removed)
            {
                await WriteUnsafeAsync(tasks, cancellationToken);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DismissAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var tasks = await ReadUnsafeAsync(cancellationToken);
            var removed = tasks.RemoveAll(x => x.Id == id) > 0;
            if (removed)
            {
                await WriteUnsafeAsync(tasks, cancellationToken);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<MediaMappingReviewTask>> ReadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(
                storePath,
                cancellationToken);

            return JsonSerializer.Deserialize<List<MediaMappingReviewTask>>(
                    json,
                    JsonOptions)
                ?? [];
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            logger.LogWarning(
                exception,
                "Could not read media mapping review tasks from {Path}.",
                storePath);
            return [];
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyCollection<MediaMappingReviewTask> tasks,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException(
                "Mapping review path has no directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath =
            $"{storePath}.tmp-{Guid.NewGuid():N}";

        try
        {
            var json = JsonSerializer.Serialize(tasks, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken);

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite);
            }

            File.Move(
                temporaryPath,
                storePath,
                overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not persist media mapping review tasks to {Path}.",
                storePath);
            throw new InvalidOperationException(
                "Mapping review tasks could not be saved.",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Required(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
