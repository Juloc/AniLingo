using System.Text.Json;

namespace AniLingo.Web.Features.Acquisition.Import;

public enum AnimeImportStatus
{
    Importing,
    Imported,
    ManualRequired,
    Failed,
    Dismissed
}

public enum AnimeImportFileStatus
{
    Planned,
    Imported,
    ManualRequired,
    Ignored,
    Failed
}

public sealed record AnimeImportFileRecord(
    string SourcePath,
    long SizeBytes,
    AnimeImportFileStatus Status,
    RequestedAnimeEpisode[] Targets,
    string[] SidecarPaths,
    string[] ReplacedPaths,
    double Confidence,
    string[] Reasons,
    string? ImportedPath,
    string? Error);

/// <summary>
/// Durable record of one completed anime download and what happened to its files. The download
/// lifecycle stays on the SABnzbd Operation; this record only carries the import plan, its
/// execution result and the manual-intervention state the owner resolves on the overview page.
/// </summary>
public sealed record AnimeImportRecord(
    Guid Id,
    Guid DownloadOperationId,
    Guid? ImportOperationId,
    Guid? AcquisitionId,
    string AnimeKey,
    string AnimeTitle,
    string? DownloadPath,
    AnimeImportStatus Status,
    AnimeImportFileRecord[] Files,
    string? Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public bool NeedsAttention =>
        Status is AnimeImportStatus.ManualRequired or AnimeImportStatus.Failed;
}

public sealed record AnimeImportStoreState(
    int Version,
    List<AnimeImportRecord> Imports)
{
    public static AnimeImportStoreState Empty() => new(1, []);
}

public sealed class AnimeImportStore
{
    public const string FileName = "imports.json";
    private const int MaxRecords = 500;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public AnimeImportStore()
        : this(new DirectoryInfo("/data/acquisition"))
    {
    }

    public AnimeImportStore(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<AnimeImportStoreState> LoadAsync(
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

    public async Task<AnimeImportRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Imports.FirstOrDefault(record => record.Id == id);

    public async Task<AnimeImportRecord?> FindByDownloadAsync(
        Guid downloadOperationId,
        CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken)).Imports
            .FirstOrDefault(record => record.DownloadOperationId == downloadOperationId);

    /// <summary>Inserts or replaces the record with the same id; the newest records are kept.</summary>
    public async Task UpsertAsync(
        AnimeImportRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Id == Guid.Empty || string.IsNullOrWhiteSpace(record.AnimeKey))
        {
            throw new InvalidDataException("Anime import record requires an id and anime key.");
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadUnsafeAsync(cancellationToken);
            var index = state.Imports.FindIndex(item => item.Id == record.Id);
            if (index >= 0)
            {
                state.Imports[index] = record;
            }
            else
            {
                state.Imports.Add(record);
            }

            if (state.Imports.Count > MaxRecords)
            {
                var keep = state.Imports
                    .OrderByDescending(item => item.NeedsAttention || item.Status == AnimeImportStatus.Importing)
                    .ThenByDescending(item => item.UpdatedAtUtc)
                    .Take(MaxRecords)
                    .ToList();
                state.Imports.Clear();
                state.Imports.AddRange(keep);
            }

            await WriteUnsafeAsync(state, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AnimeImportStoreState> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(storePath))
        {
            return AnimeImportStoreState.Empty();
        }

        try
        {
            var json = await File.ReadAllTextAsync(storePath, cancellationToken);
            var state = JsonSerializer.Deserialize<AnimeImportStoreState>(json, JsonOptions)
                ?? AnimeImportStoreState.Empty();
            return state with { Imports = state.Imports ?? [] };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Anime import state contains invalid JSON.", exception);
        }
    }

    private async Task WriteUnsafeAsync(
        AnimeImportStoreState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
        var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(state, JsonOptions),
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
