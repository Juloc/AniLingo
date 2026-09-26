using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Policy;

/// <summary>Canonical store for <see cref="AcquisitionPolicyState"/> at /data/acquisition/acquisition-policy.json.</summary>
public sealed class AcquisitionPolicyStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public AcquisitionPolicyStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        path = Path.Combine(dataRoot, "acquisition", "acquisition-policy.json");
    }

    public async Task<AcquisitionPolicyState> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task<AcquisitionPolicyState> UpdateAsync(
        Func<AcquisitionPolicyState, AcquisitionPolicyState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await gate.WaitAsync(cancellationToken);
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
            gate.Release();
        }
    }

    private async Task<AcquisitionPolicyState> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return AcquisitionPolicyState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var state = JsonSerializer.Deserialize<AcquisitionPolicyState>(json, JsonOptions)
                        ?? AcquisitionPolicyState.Empty();
            return state with
            {
                Tags = state.Tags ?? [],
                DelayProfiles = state.DelayProfiles ?? [],
                IndexerRestrictions = state.IndexerRestrictions ?? []
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Acquisition policy '{path}' is invalid JSON.", exception);
        }
    }

    private async Task SaveUnlockedAsync(AcquisitionPolicyState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}
