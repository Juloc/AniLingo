using System.Text.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Naming;

// Canonical reading naming configuration, persisted like the anime naming profiles under
// /data/acquisition (AnimeNamingProfileStore.FileName lives next to this one). Without a file the
// built-in "Original names" preset is the effective profile for every media type, which reproduces
// today's placement exactly, so a fresh install or an upgrade behaves the same as before this
// feature existed.
public sealed class ReadingNamingProfileStore
{
    public const string FileName = "reading-naming-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public ReadingNamingProfileStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public ReadingNamingProfileStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<ReadingNamingState> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task<ReadingNamingProfile> ResolveAsync(MediaAcquisitionKind kind, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return Resolve(state, kind);
    }

    public static ReadingNamingProfile Resolve(ReadingNamingState state, MediaAcquisitionKind kind)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Profiles.TryGetValue(kind.ToString(), out var profile)
            ? profile
            : ReadingNamingPresets.Default(kind);
    }

    public Task UpsertAsync(ReadingNamingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ThrowIfInvalid(profile);

        return UpdateAsync(
            state =>
            {
                var profiles = new Dictionary<string, ReadingNamingProfile>(state.Profiles, StringComparer.OrdinalIgnoreCase)
                {
                    [profile.MediaKind.ToString()] = profile
                };
                return state with { Profiles = profiles };
            },
            cancellationToken);
    }

    private async Task UpdateAsync(
        Func<ReadingNamingState, ReadingNamingState> update,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadUnsafeAsync(cancellationToken);
            var updated = update(current);
            await WriteUnsafeAsync(updated, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ReadingNamingState> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return ReadingNamingPresets.CreateDefaultState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var state = JsonSerializer.Deserialize<ReadingNamingState>(json, JsonOptions)
                ?? throw new InvalidDataException("Reading naming profile file is empty.");

            state = state with
            {
                Profiles = new Dictionary<string, ReadingNamingProfile>(
                    state.Profiles ?? new Dictionary<string, ReadingNamingProfile>(),
                    StringComparer.OrdinalIgnoreCase)
            };
            ValidateState(state);
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Reading naming profile file contains invalid JSON.", exception);
        }
    }

    private async Task WriteUnsafeAsync(ReadingNamingState state, CancellationToken cancellationToken)
    {
        ValidateState(state);

        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException("Reading naming profile path has no directory.");
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

    private static void ValidateState(ReadingNamingState state)
    {
        if (state.Version != 1)
        {
            throw new InvalidDataException($"Unsupported reading naming profile state version {state.Version}.");
        }

        foreach (var (key, profile) in state.Profiles)
        {
            if (!Enum.TryParse<MediaAcquisitionKind>(key, out var kind) || kind != profile.MediaKind)
            {
                throw new InvalidDataException($"Reading naming profile key '{key}' does not match its media kind.");
            }

            ThrowIfInvalid(profile);
        }
    }

    private static void ThrowIfInvalid(ReadingNamingProfile profile)
    {
        var errors = ReadingNamingFormatter.Validate(profile, ReadingNamingSamples.Request(profile.MediaKind));
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Reading naming profile '{profile.MediaKind}' is invalid: {string.Join("; ", errors)}");
        }
    }
}
