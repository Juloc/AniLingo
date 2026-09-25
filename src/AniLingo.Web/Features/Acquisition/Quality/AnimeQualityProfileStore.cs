using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Quality;

public sealed class AnimeQualityProfileStore
{
    private const string FileName = "quality-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public AnimeQualityProfileStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public AnimeQualityProfileStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<AnimeQualityProfileState> LoadAsync(
        CancellationToken cancellationToken = default)
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

    public async Task<AnimeQualityProfile> ResolveAsync(
        Guid? animeId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);

        var profileId = animeId is Guid id &&
                        state.AnimeProfileAssignments.TryGetValue(id.ToString("D"), out var assigned)
            ? assigned
            : state.DefaultProfileId;

        return state.Profiles.Single(profile =>
            profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(
        AnimeQualityProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfInvalid(profile);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(cancellationToken);
            var profiles = state.Profiles.ToList();
            var index = profiles.FindIndex(item =>
                item.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            await WriteUnsafeAsync(state with { Profiles = profiles.ToArray() }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetDefaultAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(cancellationToken);
            EnsureProfileExists(state, profileId);
            await WriteUnsafeAsync(
                state with { DefaultProfileId = profileId },
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AssignAnimeAsync(
        Guid animeId,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        if (animeId == Guid.Empty)
        {
            throw new ArgumentException("Anime ID must not be empty.", nameof(animeId));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(cancellationToken);
            var assignments = new Dictionary<string, string>(
                state.AnimeProfileAssignments,
                StringComparer.OrdinalIgnoreCase);
            var key = animeId.ToString("D");

            if (string.IsNullOrWhiteSpace(profileId))
            {
                assignments.Remove(key);
            }
            else
            {
                EnsureProfileExists(state, profileId);
                assignments[key] = profileId;
            }

            await WriteUnsafeAsync(
                state with { AnimeProfileAssignments = assignments },
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(cancellationToken);
            if (state.DefaultProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase) ||
                state.AnimeProfileAssignments.Values.Contains(profileId, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var remaining = state.Profiles
                .Where(profile => !profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (remaining.Length == state.Profiles.Length)
            {
                return false;
            }

            await WriteUnsafeAsync(state with { Profiles = remaining }, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AnimeQualityProfileState> ReadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return AnimeQualityProfiles.CreateDefaultState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var state = JsonSerializer.Deserialize<AnimeQualityProfileState>(json, JsonOptions)
                ?? throw new InvalidDataException("Quality profile file is empty.");

            ValidateState(state);
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Quality profile file contains invalid JSON.",
                exception);
        }
    }

    private async Task WriteUnsafeAsync(
        AnimeQualityProfileState state,
        CancellationToken cancellationToken)
    {
        ValidateState(state);

        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException("Quality profile path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
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

    private static void ValidateState(AnimeQualityProfileState state)
    {
        if (state.Version != 1)
        {
            throw new InvalidDataException(
                $"Unsupported quality profile state version {state.Version}.");
        }

        if (state.Profiles is null || state.Profiles.Length == 0)
        {
            throw new InvalidDataException("At least one quality profile is required.");
        }

        if (state.AnimeProfileAssignments is null)
        {
            throw new InvalidDataException("Anime profile assignments are missing.");
        }

        if (state.Profiles
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Quality profile IDs must be unique.");
        }

        foreach (var profile in state.Profiles)
        {
            ThrowIfInvalid(profile);
        }

        EnsureProfileExists(state, state.DefaultProfileId);

        foreach (var assignment in state.AnimeProfileAssignments)
        {
            if (!Guid.TryParse(assignment.Key, out _))
            {
                throw new InvalidDataException(
                    $"Anime profile assignment key '{assignment.Key}' is not a GUID.");
            }

            EnsureProfileExists(state, assignment.Value);
        }
    }

    private static void ThrowIfInvalid(AnimeQualityProfile profile)
    {
        var errors = AnimeReleaseScorer.ValidateProfile(profile);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                $"Quality profile '{profile.Id}' is invalid: {string.Join("; ", errors)}");
        }
    }

    private static void EnsureProfileExists(
        AnimeQualityProfileState state,
        string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) ||
            !state.Profiles.Any(profile =>
                profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Quality profile '{profileId}' does not exist.");
        }
    }
}
