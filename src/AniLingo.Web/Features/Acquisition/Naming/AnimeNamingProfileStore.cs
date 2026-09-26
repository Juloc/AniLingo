using System.Text.Json;
using System.Text.Json.Serialization;

namespace AniLingo.Web.Features.Acquisition.Naming;

// Canonical naming configuration, persisted like the other acquisition settings under
// /data/acquisition. Without a file the Sonarr default preset is the effective default profile.
public sealed class AnimeNamingProfileStore
{
    public const string FileName = "naming-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public AnimeNamingProfileStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public AnimeNamingProfileStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<AnimeNamingState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AnimeNamingResolution> ResolveAsync(
        Guid animeId,
        Guid? libraryRootId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return Resolve(state, animeId, libraryRootId);
    }

    public static AnimeNamingResolution Resolve(
        AnimeNamingState state,
        Guid animeId,
        Guid? libraryRootId)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.AnimeAssignments.TryGetValue(animeId.ToString("D"), out var assignment);
        var seriesType = assignment?.SeriesType ?? AnimeSeriesType.Anime;

        if (!string.IsNullOrWhiteSpace(assignment?.ProfileId))
        {
            return new(Find(state, assignment.ProfileId), seriesType, "anime");
        }

        if (libraryRootId is Guid rootId &&
            state.LibraryProfileAssignments.TryGetValue(rootId.ToString("D"), out var libraryProfileId))
        {
            return new(Find(state, libraryProfileId), seriesType, "library");
        }

        return new(Find(state, state.DefaultProfileId), seriesType, "default");
    }

    public Task UpsertAsync(AnimeNamingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfInvalid(profile);

        return UpdateAsync(
            state =>
            {
                var profiles = state.Profiles.ToList();
                var index = profiles.FindIndex(item => item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    profiles[index] = profile;
                }
                else
                {
                    profiles.Add(profile);
                }

                return state with { Profiles = profiles.ToArray() };
            },
            cancellationToken);
    }

    public Task SetDefaultAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        return UpdateAsync(
            state =>
            {
                EnsureProfileExists(state, profileId);
                return state with { DefaultProfileId = Find(state, profileId).Id };
            },
            cancellationToken);
    }

    public Task AssignLibraryAsync(
        Guid libraryRootId,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        if (libraryRootId == Guid.Empty)
        {
            throw new ArgumentException("Library root ID must not be empty.", nameof(libraryRootId));
        }

        return UpdateAsync(
            state =>
            {
                var assignments = new Dictionary<string, string>(
                    state.LibraryProfileAssignments,
                    StringComparer.OrdinalIgnoreCase);
                var key = libraryRootId.ToString("D");
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    assignments.Remove(key);
                }
                else
                {
                    assignments[key] = Find(state, profileId).Id;
                }

                return state with { LibraryProfileAssignments = assignments };
            },
            cancellationToken);
    }

    // A null profile inherits the library/default profile; the series type is stored either way.
    public Task AssignAnimeAsync(
        Guid animeId,
        string? profileId,
        AnimeSeriesType seriesType,
        CancellationToken cancellationToken = default)
    {
        if (animeId == Guid.Empty)
        {
            throw new ArgumentException("Anime ID must not be empty.", nameof(animeId));
        }

        if (!Enum.IsDefined(seriesType))
        {
            throw new ArgumentOutOfRangeException(nameof(seriesType));
        }

        return UpdateAsync(
            state =>
            {
                var assignments = new Dictionary<string, AnimeNamingAssignment>(
                    state.AnimeAssignments,
                    StringComparer.OrdinalIgnoreCase);
                var key = animeId.ToString("D");
                var normalizedProfile = string.IsNullOrWhiteSpace(profileId) ? null : Find(state, profileId).Id;

                if (normalizedProfile is null && seriesType == AnimeSeriesType.Anime)
                {
                    assignments.Remove(key);
                }
                else
                {
                    assignments[key] = new AnimeNamingAssignment(normalizedProfile, seriesType);
                }

                return state with { AnimeAssignments = assignments };
            },
            cancellationToken);
    }

    // Profiles in use (default, library or anime assignment) cannot be deleted.
    public async Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var deleted = false;
        await UpdateAsync(
            state =>
            {
                var inUse =
                    state.DefaultProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) ||
                    state.LibraryProfileAssignments.Values.Contains(profileId, StringComparer.OrdinalIgnoreCase) ||
                    state.AnimeAssignments.Values.Any(assignment =>
                        string.Equals(assignment.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
                var remaining = state.Profiles
                    .Where(profile => !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (inUse || remaining.Length == state.Profiles.Length)
                {
                    return state;
                }

                deleted = true;
                return state with { Profiles = remaining };
            },
            cancellationToken);
        return deleted;
    }

    private async Task UpdateAsync(
        Func<AnimeNamingState, AnimeNamingState> update,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadUnsafeAsync(cancellationToken);
            var updated = update(current);
            if (!ReferenceEquals(updated, current))
            {
                await WriteUnsafeAsync(updated, cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AnimeNamingState> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return AnimeNamingPresets.CreateDefaultState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var state = JsonSerializer.Deserialize<AnimeNamingState>(json, JsonOptions)
                ?? throw new InvalidDataException("Naming profile file is empty.");

            state = state with
            {
                LibraryProfileAssignments = new Dictionary<string, string>(
                    state.LibraryProfileAssignments ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                AnimeAssignments = new Dictionary<string, AnimeNamingAssignment>(
                    state.AnimeAssignments ?? new Dictionary<string, AnimeNamingAssignment>(),
                    StringComparer.OrdinalIgnoreCase)
            };
            ValidateState(state);
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Naming profile file contains invalid JSON.", exception);
        }
    }

    private async Task WriteUnsafeAsync(AnimeNamingState state, CancellationToken cancellationToken)
    {
        ValidateState(state);

        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException("Naming profile path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
            File.Move(temporaryPath, storePath, overwrite: true);
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

    private static void ValidateState(AnimeNamingState state)
    {
        if (state.Version != 1)
        {
            throw new InvalidDataException($"Unsupported naming profile state version {state.Version}.");
        }

        if (state.Profiles is null || state.Profiles.Length == 0)
        {
            throw new InvalidDataException("At least one naming profile is required.");
        }

        if (state.Profiles.GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Naming profile IDs must be unique.");
        }

        foreach (var profile in state.Profiles)
        {
            ThrowIfInvalid(profile);
        }

        EnsureProfileExists(state, state.DefaultProfileId);

        foreach (var (key, profileId) in state.LibraryProfileAssignments)
        {
            if (!Guid.TryParse(key, out _))
            {
                throw new InvalidDataException($"Library naming assignment key '{key}' is not a GUID.");
            }

            EnsureProfileExists(state, profileId);
        }

        foreach (var (key, assignment) in state.AnimeAssignments)
        {
            if (!Guid.TryParse(key, out _))
            {
                throw new InvalidDataException($"Anime naming assignment key '{key}' is not a GUID.");
            }

            if (assignment is null || !Enum.IsDefined(assignment.SeriesType))
            {
                throw new InvalidDataException($"Anime naming assignment '{key}' is invalid.");
            }

            if (assignment.ProfileId is not null)
            {
                EnsureProfileExists(state, assignment.ProfileId);
            }
        }
    }

    private static void ThrowIfInvalid(AnimeNamingProfile profile)
    {
        var errors = AnimeNamingFormatter.Validate(profile);
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Naming profile '{profile.Id}' is invalid: {string.Join("; ", errors)}");
        }
    }

    private static AnimeNamingProfile Find(AnimeNamingState state, string profileId)
    {
        EnsureProfileExists(state, profileId);
        return state.Profiles.Single(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureProfileExists(AnimeNamingState state, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) ||
            !state.Profiles.Any(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Naming profile '{profileId}' does not exist.");
        }
    }
}
