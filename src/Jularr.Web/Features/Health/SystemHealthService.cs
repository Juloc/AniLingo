using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Admin;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Storage;
using Jularr.Web.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Health;

/// <summary>
/// Read-only diagnostics for Admin &gt; Health (#528, part of epic #510): database, storage,
/// external tools and the background job queue. Every check reuses an existing store or service
/// (<see cref="LibraryRootAvailabilityService"/>, <see cref="MediaProcessRunner"/>,
/// <see cref="OperationStore"/>, <see cref="IndexerStore"/>, <see cref="DownloadClientStore"/>,
/// <see cref="AcquisitionHealthStore"/>); this only aggregates and classifies results as
/// OK/warning/error instead of raw logs. AI reachability is read straight from
/// <c>CodexCliProvider</c> by the page, not duplicated here.
/// </summary>
public sealed class SystemHealthService(
    AppDbContext db,
    LibraryRootAvailabilityService availability,
    MediaProcessRunner processRunner,
    IndexerStore indexerStore,
    DownloadClientStore downloadClientStore,
    AcquisitionHealthStore acquisitionHealth)
{
    // Below this, a full library root going offline would leave no room to finish an import.
    private const long ErrorFreeBytes = 2_000_000_000; // 2 GB
    private const long WarningFreeBytes = 10_000_000_000; // 10 GB
    private static readonly TimeSpan ToolProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<SystemHealthSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var database = await CheckDatabaseAsync(cancellationToken);
        var dataVolume = CheckDataVolume();
        var roots = await CheckStorageRootsAsync(cancellationToken);
        var ffmpeg = await CheckToolAsync("ffmpeg", cancellationToken);
        var ffprobe = await CheckToolAsync("ffprobe", cancellationToken);
        var jobQueue = await CheckJobQueueAsync(cancellationToken);
        var acquisitionSummary = await CheckAcquisitionAsync(cancellationToken);

        return new SystemHealthSnapshot(
            database,
            dataVolume,
            roots,
            ffmpeg,
            ffprobe,
            jobQueue,
            acquisitionSummary);
    }

    private async Task<DatabaseHealth> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        bool reachable;
        string? error = null;
        try
        {
            reachable = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            reachable = false;
            error = exception.Message;
        }

        long? fileSize = null;
        var connectionString = db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource) &&
                !dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var info = new FileInfo(dataSource);
                    if (info.Exists)
                    {
                        fileSize = info.Length;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return new DatabaseHealth(
            reachable ? HealthState.Ok : HealthState.Error,
            reachable,
            fileSize,
            error);
    }

    private static DataVolumeHealth CheckDataVolume()
    {
        var (free, total) = AdminServerLoad.DataVolumeSpace();
        var state = free switch
        {
            null => HealthState.Warning,
            <= ErrorFreeBytes => HealthState.Error,
            <= WarningFreeBytes => HealthState.Warning,
            _ => HealthState.Ok
        };

        return new DataVolumeHealth(state, free, total);
    }

    private async Task<IReadOnlyList<StorageRootHealthRow>> CheckStorageRootsAsync(
        CancellationToken cancellationToken)
    {
        var roots = await db.LibraryRoots
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var rows = new List<StorageRootHealthRow>(roots.Count);
        foreach (var root in roots)
        {
            var snapshot = availability.GetCached(root)
                ?? await availability.CheckAsync(root.Id, force: false, cancellationToken);
            var online = snapshot?.IsAvailable == true;
            var writable = online && TryWrite(root.Path);

            rows.Add(new StorageRootHealthRow(root.Id, root.Name, root.Path, online, writable));
        }

        return rows;
    }

    // A small, self-cleaning marker file: the only non-destructive way to confirm Jularr can
    // actually write to a mounted root rather than only list its directory.
    private static bool TryWrite(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return false;
        }

        var marker = Path.Combine(rootPath, $".jularr-health-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(marker))
            {
            }

            File.Delete(marker);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private async Task<ExternalToolStatus> CheckToolAsync(string executable, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(executable, ["-version"], ToolProbeTimeout, cancellationToken);
        if (result is null)
        {
            return new ExternalToolStatus(executable, false, null, $"{executable} was not found or did not respond.");
        }

        if (result.ExitCode != 0)
        {
            return new ExternalToolStatus(executable, false, null, result.ErrorSummary);
        }

        return new ExternalToolStatus(executable, true, ParseToolVersion(result.Output), null);
    }

    // ffmpeg/ffprobe both print "<name> version <version> Copyright ..." as their first line.
    public static string? ParseToolVersion(string output)
    {
        var span = output.AsSpan();
        var newline = span.IndexOf('\n');
        var firstLine = (newline >= 0 ? span[..newline] : span).ToString().Trim();

        const string marker = " version ";
        var index = firstLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return firstLine.Length > 0 ? firstLine : null;
        }

        var rest = firstLine[(index + marker.Length)..].Trim();
        var space = rest.IndexOf(' ');
        var version = space < 0 ? rest : rest[..space];
        return version.Trim();
    }

    private async Task<JobQueueHealth> CheckJobQueueAsync(CancellationToken cancellationToken)
    {
        var summary = await new OperationStore(db).GetSummaryAsync(cancellationToken);
        return new JobQueueHealth(summary.Running, summary.Queued, summary.Interrupted);
    }

    private async Task<AcquisitionDependencySummary> CheckAcquisitionAsync(CancellationToken cancellationToken)
    {
        var indexers = await indexerStore.LoadAllAsync(cancellationToken);
        var healthyIndexers = 0;
        foreach (var indexer in indexers)
        {
            if (await acquisitionHealth.IsHealthyAsync(AcquisitionHealthKind.Indexer, indexer.Id, cancellationToken))
            {
                healthyIndexers++;
            }
        }

        var downloadClients = await downloadClientStore.LoadAllAsync(cancellationToken);
        var healthyClients = 0;
        foreach (var client in downloadClients)
        {
            if (await acquisitionHealth.IsHealthyAsync(AcquisitionHealthKind.DownloadClient, client.Id, cancellationToken))
            {
                healthyClients++;
            }
        }

        return new AcquisitionDependencySummary(healthyIndexers, indexers.Count, healthyClients, downloadClients.Count);
    }
}
