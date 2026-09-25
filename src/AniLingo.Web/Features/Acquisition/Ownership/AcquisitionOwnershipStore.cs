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
                        StringComparer.OrdinalIgnoreCase)
                };
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Ownership state '{_path}' is invalid JSON.", ex);
            }
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
}
