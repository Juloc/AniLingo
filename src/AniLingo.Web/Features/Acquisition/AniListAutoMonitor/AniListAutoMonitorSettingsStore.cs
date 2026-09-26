using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.AniListAutoMonitor;

public sealed class AniListAutoMonitorSettingsStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AniListAutoMonitorSettingsStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        path = Path.Combine(dataRoot, "acquisition", "anilist-auto-monitor.json");
    }

    public async Task<AniListAutoMonitorState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetEnabledAsync(string profileId, bool enabled, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnlockedAsync(cancellationToken);
            var profiles = new Dictionary<string, AniListAutoMonitorProfileSettings>(current.Profiles, StringComparer.Ordinal)
            {
                [profileId] = new(enabled, enabled ? now : null)
            };
            await SaveUnlockedAsync(current with { Profiles = profiles }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AniListAutoMonitorState> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return AniListAutoMonitorState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var state = JsonSerializer.Deserialize<AniListAutoMonitorState>(json, JsonOptions)
                        ?? AniListAutoMonitorState.Empty();
            return state with { Profiles = new Dictionary<string, AniListAutoMonitorProfileSettings>(state.Profiles, StringComparer.Ordinal) };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"AniList auto-monitor settings '{path}' is invalid JSON.", exception);
        }
    }

    private async Task SaveUnlockedAsync(AniListAutoMonitorState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}
