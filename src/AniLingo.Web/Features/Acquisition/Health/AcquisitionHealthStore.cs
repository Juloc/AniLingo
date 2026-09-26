using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Health;

/// <summary>
/// The one canonical health record for indexers and download clients.
/// Nothing else decides whether an entry is skipped as unhealthy.
/// </summary>
public sealed class AcquisitionHealthStore
{
    public const string FileName = "health.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public AcquisitionHealthStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public AcquisitionHealthStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<AcquisitionHealthState> LoadAsync(
        CancellationToken cancellationToken = default)
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

    public async Task<AcquisitionHealthStatus?> GetAsync(
        AcquisitionHealthKind kind,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return state.Statuses.FirstOrDefault(status => status.Kind == kind && status.EntryId == entryId);
    }

    public async Task<bool> IsHealthyAsync(
        AcquisitionHealthKind kind,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var status = await GetAsync(kind, entryId, cancellationToken);
        // Unknown (never checked yet) is treated as healthy so a brand new
        // entry is not skipped before the first health check runs.
        return status?.IsHealthy ?? true;
    }

    public async Task RecordAsync(
        AcquisitionHealthStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnlockedAsync(cancellationToken);
            var statuses = state.Statuses
                .Where(existing => !(existing.Kind == status.Kind && existing.EntryId == status.EntryId))
                .Append(status)
                .ToArray();

            await WriteUnlockedAsync(new AcquisitionHealthState(statuses), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(
        AcquisitionHealthKind kind,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadUnlockedAsync(cancellationToken);
            var statuses = state.Statuses
                .Where(existing => !(existing.Kind == kind && existing.EntryId == entryId))
                .ToArray();

            await WriteUnlockedAsync(new AcquisitionHealthState(statuses), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AcquisitionHealthState> LoadUnlockedAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return new AcquisitionHealthState([]);
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var statuses = JsonSerializer.Deserialize<AcquisitionHealthStatus[]>(json, JsonOptions);
            return new AcquisitionHealthState(statuses ?? []);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Acquisition health state contains invalid JSON.", exception);
        }
    }

    private async Task WriteUnlockedAsync(
        AcquisitionHealthState state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(storePath)
            ?? throw new InvalidOperationException("Acquisition health path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(state.Statuses, JsonOptions),
                cancellationToken);
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
}
