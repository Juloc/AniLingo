using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Acquisition.Monitoring;

/// <summary>
/// The JSON settings store for monitoring state, one file per media type under
/// <c>/data/acquisition</c>. Anime keeps the historical <c>monitoring.json</c> path (and shape) so
/// existing state loads unchanged; every other media type gets its own <c>monitoring-{kind}.json</c>,
/// so the kinds never share a file or collide on work keys. State is a single-writer JSON document
/// written atomically via a temp file + move, guarded by an in-process gate.
/// </summary>
public sealed class MonitoringStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public MonitoringStore(string dataRoot, MediaAcquisitionKind kind = MediaAcquisitionKind.Anime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        // Anime is the historical single-file store; its path is preserved for parity. Other media
        // types get a per-kind file next to it.
        var fileName = kind == MediaAcquisitionKind.Anime
            ? "monitoring.json"
            : $"monitoring-{AcquisitionAccessNames.Kind(kind)}.json";
        _path = Path.Combine(dataRoot, "acquisition", fileName);
    }

    public async Task<MonitoringState> LoadAsync(CancellationToken cancellationToken = default)
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
        MonitoringState state,
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

    public async Task<MonitoringState> UpdateAsync(
        Func<MonitoringState, MonitoringState> update,
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

    public async Task<bool> RekeyAnimeAsync(
        string oldKey,
        string newKey,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadAsync(cancellationToken);
        if (ReferenceEquals(MonitoringEngine.RekeyAnime(current, oldKey, newKey), current))
        {
            return false;
        }

        await UpdateAsync(state => MonitoringEngine.RekeyAnime(state, oldKey, newKey), cancellationToken);
        return true;
    }

    private async Task<MonitoringState> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return MonitoringState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var state = JsonSerializer.Deserialize<MonitoringState>(json, JsonOptions);
            return state ?? MonitoringState.Empty();
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"Monitoring state '{_path}' is invalid JSON.");
        }
    }
}
