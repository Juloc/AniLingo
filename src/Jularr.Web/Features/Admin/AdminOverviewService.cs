using System.Diagnostics;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Playback.Decision;
using Jularr.Web.Features.Storage;
using Jularr.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Admin;

/// <summary>
/// One status row the Admin overview answers ("is anything broken", "who is watching", ...).
/// <see cref="Count"/> is null for rows that report a state instead of a count (server load).
/// </summary>
public sealed record AdminOverviewSnapshot(
    int FailedOperations,
    int ActiveDownloads,
    int ActiveProcessing,
    int OpenAcquisitionRequests,
    int UnresolvedMappings,
    int MissingLearningText,
    int StorageRootsOffline,
    int BlockedJobs,
    int WatchingNow,
    int TranscodingNow,
    double ProcessCpuPercent,
    long ProcessWorkingSetBytes,
    long? DataVolumeFreeBytes,
    long? DataVolumeTotalBytes)
{
    public static readonly AdminOverviewSnapshot Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
}

/// <summary>
/// Read-only counts for the Admin landing page (#518). Every count reuses an existing store's
/// public query; nothing here owns data or writes anything.
/// </summary>
public sealed class AdminOverviewService(
    AppDbContext db,
    AcquisitionAccessStore acquisitionAccess,
    MediaMappingReviewStore mappingReview,
    LibraryRootAvailabilityService storageAvailability,
    SubtitleImportService subtitleImport,
    PlaybackStreamSessionStore sessions)
{
    // The store clamps to 500 regardless; this just avoids an arbitrary smaller default.
    private const int OpenRequestScanLimit = 500;

    public async Task<AdminOverviewSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var operations = new OperationStore(db);
        var summary = await operations.GetSummaryAsync(cancellationToken);
        var processing = await GetActiveProcessingCountAsync(operations, cancellationToken);

        var openRequests = await acquisitionAccess.ListAsync(
            kind: null,
            requestedByProfileId: null,
            openOnly: true,
            limit: OpenRequestScanLimit,
            cancellationToken);

        var mappingTasks = await mappingReview.ListAsync(cancellationToken);
        var subtitleCoverage = await subtitleImport.GetCoverageAsync(cancellationToken);

        var roots = await db.LibraryRoots.AsNoTracking().ToListAsync(cancellationToken);
        var offlineRoots = roots.Count(root => IsOffline(storageAvailability.GetCached(root)));

        var allSessions = sessions.ListAll();
        var transcoding = allSessions.Count(x => x.Plan.Mode == PlaybackDeliveryMode.Transcode);

        var (cpuPercent, workingSetBytes) = AdminServerLoad.Sample();
        var (freeBytes, totalBytes) = AdminServerLoad.DataVolumeSpace();

        return new AdminOverviewSnapshot(
            FailedOperations: summary.Failed,
            ActiveDownloads: summary.ActiveDownloads,
            ActiveProcessing: processing,
            OpenAcquisitionRequests: openRequests.Count,
            UnresolvedMappings: mappingTasks.Count,
            MissingLearningText: subtitleCoverage.MissingEpisodes,
            StorageRootsOffline: offlineRoots,
            BlockedJobs: summary.Interrupted,
            WatchingNow: allSessions.Count,
            TranscodingNow: transcoding,
            ProcessCpuPercent: cpuPercent,
            ProcessWorkingSetBytes: workingSetBytes,
            DataVolumeFreeBytes: freeBytes,
            DataVolumeTotalBytes: totalBytes);
    }

    // Running background work that is not itself a download (imports, scans, remuxes, ...).
    // OperationSummary only has a combined "Running" count, so this reuses the existing "active"
    // list query and filters the small in-memory result rather than adding a new store method.
    private static async Task<int> GetActiveProcessingCountAsync(
        OperationStore operations,
        CancellationToken cancellationToken)
    {
        var active = await operations.ListAsync(
            new OperationListFilter(View: "active", Limit: 500),
            cancellationToken);
        return active.Count(x => !x.IsDownload);
    }

    private static bool IsOffline(LibraryRootAvailabilitySnapshot? snapshot) =>
        snapshot is { State: StorageAvailabilityState.Offline or StorageAvailabilityState.Unreachable or StorageAvailabilityState.FileMissing };
}

/// <summary>Process CPU/memory and the data volume's free space, sampled cheaply for the overview.</summary>
internal static class AdminServerLoad
{
    private static readonly object Gate = new();
    private static TimeSpan lastCpuTime;
    private static DateTime lastSampleUtc;
    private static bool sampled;

    /// <summary>
    /// CPU% since the previous sample (0 on the very first call in a process's lifetime) and the
    /// current working set. Two samples are needed for a CPU rate, so the first call establishes
    /// the baseline instead of guessing.
    /// </summary>
    public static (double CpuPercent, long WorkingSetBytes) Sample()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuNow = process.TotalProcessorTime;
        var workingSet = process.WorkingSet64;
        var nowUtc = DateTime.UtcNow;

        lock (Gate)
        {
            var percent = 0d;
            if (sampled)
            {
                var elapsedWallMs = (nowUtc - lastSampleUtc).TotalMilliseconds;
                var elapsedCpuMs = (cpuNow - lastCpuTime).TotalMilliseconds;
                if (elapsedWallMs > 0 && Environment.ProcessorCount > 0)
                {
                    percent = elapsedCpuMs / (elapsedWallMs * Environment.ProcessorCount) * 100;
                }
            }

            lastCpuTime = cpuNow;
            lastSampleUtc = nowUtc;
            sampled = true;

            return (Math.Clamp(percent, 0, 100), workingSet);
        }
    }

    /// <summary>Free/total bytes of the volume behind <c>/data</c>; the container mounts media there.</summary>
    public static (long? Free, long? Total) DataVolumeSpace()
    {
        foreach (var candidate in DataVolumeCandidates())
        {
            try
            {
                if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate))
                {
                    continue;
                }

                // DriveInfo wants a root ("/" or "C:\"), not an arbitrary path inside the volume.
                var root = Path.GetPathRoot(candidate);
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    continue;
                }

                return (drive.AvailableFreeSpace, drive.TotalSize);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        return (null, null);
    }

    /// <summary>Free/total bytes of the volume behind <paramref name="path"/>, or nulls when it cannot be read.</summary>
    public static (long? Free, long? Total) VolumeSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return (null, null);
            }

            // Windows wants the drive root; elsewhere the volume is resolved from any path inside it.
            var name = OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : path;
            if (string.IsNullOrEmpty(name))
            {
                return (null, null);
            }

            var drive = new DriveInfo(name);
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : (null, null);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }

        return (null, null);
    }

    // "/data" is the production mount; the base directory is the fallback so local development
    // (no such path on Windows) still reports something instead of nothing.
    private static IEnumerable<string> DataVolumeCandidates()
    {
        yield return "/data";
        yield return AppContext.BaseDirectory;
    }
}
