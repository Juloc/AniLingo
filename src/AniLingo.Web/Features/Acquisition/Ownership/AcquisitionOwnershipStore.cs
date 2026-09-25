using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Ownership;

public sealed class AcquisitionOwnershipStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AcquisitionOwnershipStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _path = Path.Combine(dataRoot, "acquisition", "ownership.json");
    }

    public async Task<AcquisitionOwnershipState> LoadAsync(CancellationToken cancellationToken = default)
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
        AcquisitionOwnershipState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveUnlockedAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Atomic read-modify-write so concurrent writers (migration controls, acquisition jobs)
    // cannot overwrite each other's ownership changes.
    public async Task<AcquisitionOwnershipState> UpdateAsync(
        Func<AcquisitionOwnershipState, AcquisitionOwnershipState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnlockedAsync(cancellationToken);
            var updated = update(current);
            if (!ReferenceEquals(updated, current))
            {
                await SaveUnlockedAsync(updated, cancellationToken);
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AcquisitionOwnershipState> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return AcquisitionOwnershipState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken);
            var state = JsonSerializer.Deserialize<AcquisitionOwnershipState>(json, JsonOptions)
                        ?? AcquisitionOwnershipState.Empty();

            return state with
            {
                Anime = new Dictionary<string, AnimeManagementAssignment>(
                    state.Anime,
                    StringComparer.OrdinalIgnoreCase),
                Jobs = new Dictionary<string, AcquisitionOwnership>(
                    state.Jobs,
                    StringComparer.OrdinalIgnoreCase),
                Paths = new Dictionary<string, ManagedMediaPath>(
                    state.Paths,
                    StringComparer.OrdinalIgnoreCase),
                MigrationLog = state.MigrationLog ?? []
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Ownership state '{_path}' is invalid JSON.", ex);
        }
    }

    private async Task SaveUnlockedAsync(
        AcquisitionOwnershipState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(state, JsonOptions),
            cancellationToken);
        File.Move(temporary, _path, overwrite: true);
    }
}
