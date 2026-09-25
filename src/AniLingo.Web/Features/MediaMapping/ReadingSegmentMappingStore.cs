using System.Text.Json;

namespace AniLingo.Web.Features.MediaMapping;

public sealed record ReadingMediaSegmentMapping(
    Guid Id,
    string MediaType,
    string LocalId,
    double LocalChapterStart,
    double LocalChapterEnd,
    int RemoteChapterStart,
    string Provider,
    string ExternalId,
    string? PreferredTitle,
    int? RemoteChapterCount,
    int? LocalVolumeStart,
    int? LocalVolumeEnd,
    int? RemoteVolumeStart,
    DateTimeOffset UpdatedAt)
{
    public string Source { get; init; } = "manual";

    public bool ContainsChapter(double chapterNumber) =>
        chapterNumber >= LocalChapterStart &&
        chapterNumber <= LocalChapterEnd;

    public bool ContainsVolume(int volumeNumber) =>
        LocalVolumeStart is not null &&
        LocalVolumeEnd is not null &&
        volumeNumber >= LocalVolumeStart.Value &&
        volumeNumber <= LocalVolumeEnd.Value;
}

public sealed record ReadingSegmentResolution(
    bool CanSync,
    string? Provider,
    string? ExternalId,
    string? PreferredTitle,
    int Progress,
    int? VolumeProgress,
    int? RemoteChapterCount,
    string? Reason)
{
    public static ReadingSegmentResolution Blocked(string reason) =>
        new(false, null, null, null, 0, null, null, reason);
}

public sealed class ReadingSegmentMappingStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly ILogger<ReadingSegmentMappingStore> logger;
    private readonly string storePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ReadingSegmentMappingStore(
        ILogger<ReadingSegmentMappingStore> logger)
        : this(
            logger,
            new DirectoryInfo("/data/integrations/anilist"))
    {
    }

    public ReadingSegmentMappingStore(
        ILogger<ReadingSegmentMappingStore> logger,
        DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(directory);

        this.logger = logger;
        storePath = Path.Combine(
            directory.FullName,
            "reading-segment-mappings.json");
    }

    public async Task<IReadOnlyList<ReadingMediaSegmentMapping>> ListAsync(
        string? mediaType = null,
        string? localId = null,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            IEnumerable<ReadingMediaSegmentMapping> mappings =
                await ReadUnsafeAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(mediaType))
            {
                mappings = mappings.Where(x =>
                    string.Equals(
                        x.MediaType,
                        mediaType.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(localId))
            {
                mappings = mappings.Where(x =>
                    string.Equals(
                        x.LocalId,
                        localId.Trim(),
                        StringComparison.Ordinal));
            }

            return mappings
                .OrderBy(x => x.MediaType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.LocalId, StringComparer.Ordinal)
                .ThenBy(x => x.LocalChapterStart)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ReadingMediaSegmentMapping> AddAsync(
        ReadingMediaSegmentMapping mapping,
        CancellationToken cancellationToken = default)
    {
        Validate(mapping);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var mappings = await ReadUnsafeAsync(cancellationToken);

            var overlap = mappings.Any(existing =>
                string.Equals(
                    existing.MediaType,
                    mapping.MediaType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    existing.LocalId,
                    mapping.LocalId,
                    StringComparison.Ordinal) &&
                mapping.LocalChapterStart <= existing.LocalChapterEnd &&
                mapping.LocalChapterEnd >= existing.LocalChapterStart);

            if (overlap)
            {
                throw new InvalidOperationException(
                    "This local chapter range overlaps an existing external-media segment.");
            }

            var normalized = mapping with
            {
                Id = mapping.Id == Guid.Empty
                    ? Guid.NewGuid()
                    : mapping.Id,
                MediaType = NormalizeMediaType(mapping.MediaType),
                LocalId = mapping.LocalId.Trim(),
                Provider = mapping.Provider.Trim().ToLowerInvariant(),
                ExternalId = mapping.ExternalId.Trim(),
                PreferredTitle = NormalizeOptional(mapping.PreferredTitle),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            mappings.Add(normalized);
            await WriteUnsafeAsync(mappings, cancellationToken);
            return normalized;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ReplaceAutomaticAsync(
        string mediaType,
        string localId,
        IReadOnlyList<ReadingMediaSegmentMapping> plannedMappings,
        CancellationToken cancellationToken = default)
    {
        var normalizedMediaType = NormalizeMediaType(mediaType);
        var normalizedLocalId = localId.Trim();

        if (plannedMappings.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one automatic reading segment is required.");
        }

        foreach (var mapping in plannedMappings)
        {
            Validate(mapping);

            if (!string.Equals(
                    NormalizeMediaType(mapping.MediaType),
                    normalizedMediaType,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    mapping.LocalId.Trim(),
                    normalizedLocalId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "All automatic segments must target the same local work.");
            }
        }

        var ordered = plannedMappings
            .OrderBy(x => x.LocalChapterStart)
            .ToArray();

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].LocalChapterStart <=
                ordered[index - 1].LocalChapterEnd)
            {
                throw new InvalidOperationException(
                    "Automatic reading segments must not overlap.");
            }
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var mappings = await ReadUnsafeAsync(cancellationToken);
            var existing = mappings
                .Where(x =>
                    string.Equals(
                        x.MediaType,
                        normalizedMediaType,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        x.LocalId,
                        normalizedLocalId,
                        StringComparison.Ordinal))
                .ToArray();

            if (existing.Any(x =>
                    !string.Equals(
                        x.Source,
                        "automatic",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            mappings.RemoveAll(x =>
                string.Equals(
                    x.MediaType,
                    normalizedMediaType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.LocalId,
                    normalizedLocalId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    x.Source,
                    "automatic",
                    StringComparison.OrdinalIgnoreCase));

            var now = DateTimeOffset.UtcNow;
            foreach (var mapping in ordered)
            {
                mappings.Add(mapping with
                {
                    Id = mapping.Id == Guid.Empty
                        ? Guid.NewGuid()
                        : mapping.Id,
                    MediaType = normalizedMediaType,
                    LocalId = normalizedLocalId,
                    Provider = mapping.Provider.Trim().ToLowerInvariant(),
                    ExternalId = mapping.ExternalId.Trim(),
                    PreferredTitle = NormalizeOptional(mapping.PreferredTitle),
                    UpdatedAt = now,
                    Source = "automatic"
                });
            }

            await WriteUnsafeAsync(mappings, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var mappings = await ReadUnsafeAsync(cancellationToken);
            var removed = mappings.RemoveAll(x => x.Id == id) > 0;
            if (removed)
            {
                await WriteUnsafeAsync(mappings, cancellationToken);
            }

            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ReadingSegmentResolution?> ResolveAsync(
        string mediaType,
        string localId,
        double localChapterNumber,
        int? localVolumeNumber,
        int position,
        int completedThreshold,
        CancellationToken cancellationToken = default)
    {
        var mappings = await ListAsync(
            mediaType,
            localId,
            cancellationToken);

        if (mappings.Count == 0)
        {
            return null;
        }

        var matches = mappings
            .Where(x => x.ContainsChapter(localChapterNumber))
            .ToArray();

        if (matches.Length != 1)
        {
            return ReadingSegmentResolution.Blocked(
                matches.Length == 0
                    ? "The current local chapter is outside all configured AniList segment mappings."
                    : "More than one AniList segment mapping matches the current local chapter.");
        }

        var mapping = matches[0];
        var progress = AutomaticMediaMatcher.ResolveMappedProgress(
            localChapterNumber,
            position,
            completedThreshold,
            new MediaSegmentMapping(
                mapping.LocalChapterStart,
                mapping.LocalChapterEnd,
                mapping.RemoteChapterStart));

        if (!progress.CanSync)
        {
            return new ReadingSegmentResolution(
                false,
                mapping.Provider,
                mapping.ExternalId,
                mapping.PreferredTitle,
                progress.Progress,
                null,
                mapping.RemoteChapterCount,
                progress.Reason);
        }

        int? volumeProgress = null;
        if (localVolumeNumber is not null &&
            mapping.LocalVolumeStart is not null &&
            mapping.LocalVolumeEnd is not null &&
            mapping.RemoteVolumeStart is not null)
        {
            if (!mapping.ContainsVolume(localVolumeNumber.Value))
            {
                return new ReadingSegmentResolution(
                    false,
                    mapping.Provider,
                    mapping.ExternalId,
                    mapping.PreferredTitle,
                    progress.Progress,
                    null,
                    mapping.RemoteChapterCount,
                    "The current local volume is outside the configured AniList volume range.");
            }

            volumeProgress = checked(
                mapping.RemoteVolumeStart.Value +
                (localVolumeNumber.Value - mapping.LocalVolumeStart.Value));
        }

        return new ReadingSegmentResolution(
            true,
            mapping.Provider,
            mapping.ExternalId,
            mapping.PreferredTitle,
            progress.Progress,
            volumeProgress,
            mapping.RemoteChapterCount,
            null);
    }

    private static void Validate(ReadingMediaSegmentMapping mapping)
    {
        var mediaType = NormalizeMediaType(mapping.MediaType);
        if (mediaType is not ("manga" or "novel"))
        {
            throw new InvalidOperationException(
                "Reading segment mappings currently support manga or novel media types.");
        }

        if (string.IsNullOrWhiteSpace(mapping.LocalId))
        {
            throw new InvalidOperationException("Local media ID is required.");
        }

        if (mapping.LocalChapterStart <= 0 ||
            mapping.LocalChapterEnd < mapping.LocalChapterStart)
        {
            throw new InvalidOperationException(
                "Local chapter range is invalid.");
        }

        if (mapping.RemoteChapterStart <= 0)
        {
            throw new InvalidOperationException(
                "Remote chapter start must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(mapping.Provider) ||
            string.IsNullOrWhiteSpace(mapping.ExternalId))
        {
            throw new InvalidOperationException(
                "External media provider and ID are required.");
        }

        var localSpan = mapping.LocalChapterEnd - mapping.LocalChapterStart;
        if (Math.Abs(localSpan - Math.Round(localSpan)) > 0.0001)
        {
            throw new InvalidOperationException(
                "The local chapter range must preserve integer offsets.");
        }

        var remoteEnd = mapping.RemoteChapterStart + (int)Math.Round(localSpan);
        if (mapping.RemoteChapterCount is > 0 &&
            remoteEnd > mapping.RemoteChapterCount.Value)
        {
            throw new InvalidOperationException(
                "The mapped remote chapter range exceeds the known AniList chapter count.");
        }

        var hasAnyVolumeField =
            mapping.LocalVolumeStart is not null ||
            mapping.LocalVolumeEnd is not null ||
            mapping.RemoteVolumeStart is not null;

        if (hasAnyVolumeField)
        {
            if (mapping.LocalVolumeStart is not > 0 ||
                mapping.LocalVolumeEnd is not > 0 ||
                mapping.RemoteVolumeStart is not > 0 ||
                mapping.LocalVolumeEnd < mapping.LocalVolumeStart)
            {
                throw new InvalidOperationException(
                    "Volume offsets require positive local start/end and remote start values.");
            }
        }
    }

    private async Task<List<ReadingMediaSegmentMapping>> ReadUnsafeAsync(
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
            return JsonSerializer.Deserialize<List<ReadingMediaSegmentMapping>>(
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
                "Could not read reading segment mappings from {Path}.",
                storePath);
            return [];
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyCollection<ReadingMediaSegmentMapping> mappings,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException(
                "Reading segment mapping path has no directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";

        try
        {
            var json = JsonSerializer.Serialize(mappings, JsonOptions);
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
                "Could not persist reading segment mappings to {Path}.",
                storePath);
            throw new InvalidOperationException(
                "Reading segment mappings could not be saved.",
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

    private static string NormalizeMediaType(string value) =>
        value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}
