using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Monitoring;

public sealed class AnimeMonitoringStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AnimeMonitoringStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _path = Path.Combine(dataRoot, "acquisition", "monitoring.json");
    }

    public async Task<AnimeMonitoringState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        AnimeMonitoringState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(state, JsonOptions),
                cancellationToken);

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AnimeMonitoringState> UpdateAsync(
        Func<AnimeMonitoringState, AnimeMonitoringState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnlockedAsync(cancellationToken);
            var updated = update(state);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(updated, JsonOptions),
                cancellationToken);
            File.Move(temporary, _path, overwrite: true);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AnimeMonitoringState> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return AnimeMonitoringState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var state = JsonSerializer.Deserialize<AnimeMonitoringState>(json, JsonOptions);
            return state ?? AnimeMonitoringState.Empty();
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"Monitoring state '{_path}' is invalid JSON.");
        }
    }
}
