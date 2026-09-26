using System.Text.Json;
using System.Text.Json.Serialization;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Storage;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

public enum LibraryScanTrigger
{
    Manual = 1,
    Startup = 2,
    Watch = 3,
    Periodic = 4,
    Retry = 5
}

public sealed record LibraryScanRequest(
    Guid RootId,
    LibraryScanTrigger Trigger,
    string? Folder = null,
    string? ProfileId = null);

public sealed record LibraryScanCounters(
    int MediaFiles,
    int Discovered,
    int Updated,
    int Removed,
    int Skipped,
    int Subtitles,
    int Artwork,
    int Metadata,
    int Errors,
    int MediaAnalyzed = 0,
    int MediaAnalysisFailed = 0);

// The structured document persisted in Operations.Details for every library scan run.
// It is the one place that carries the root, scope, trigger, phase and counters of a run.
public sealed record LibraryScanDetails(
    Guid RootId,
    string? Folder,
    LibraryScanTrigger Trigger,
    LibraryScanPhase Phase,
    int FilesProcessed,
    int FilesTotal,
    int Warnings,
    LibraryScanCounters? Counters)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static LibraryScanDetails? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LibraryScanDetails>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public enum LibraryScanQueueOutcome
{
    Queued = 1,
    AlreadyActive = 2,
    RootUnavailable = 3,
    RootNotFound = 4,
    RootDisabled = 5
}

public sealed record LibraryScanQueueResult(
    LibraryScanQueueOutcome Outcome,
    Guid? OperationId,
    string Message)
{
    public bool Queued => Outcome == LibraryScanQueueOutcome.Queued;
}

public sealed record ActiveLibraryScan(Guid RootId, string? Folder, Guid OperationId);

// Surfaces a scan failure with a message that is safe to persist in the operation record.
public sealed class LibraryScanException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// One entry point for every library scan: startup, manual, watcher and periodic runs are
// coalesced per root here, queued on the maintenance lane and recorded as Operations.
public sealed class LibraryScanCoordinator(
    IServiceScopeFactory scopeFactory,
    BackgroundJobQueue jobs,
    ILogger<LibraryScanCoordinator> logger)
{
    public const string OperationKind = "library-scan";
    public const string OperationCategory = "Library";
    public const int HistoryLimit = 200;
    private static readonly TimeSpan ProgressWriteInterval = TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly List<Reservation> active = [];

    public IReadOnlyList<ActiveLibraryScan> GetActive(Guid rootId)
    {
        lock (gate)
        {
            return active
                .Where(x => x.RootId == rootId)
                .Select(x => new ActiveLibraryScan(x.RootId, x.Folder, x.OperationId))
                .ToArray();
        }
    }

    public async Task<LibraryScanQueueResult> QueueAsync(
        LibraryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = NormalizeFolder(request.Folder);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = await db.LibraryRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.RootId, cancellationToken);
        if (root is null)
        {
            return new LibraryScanQueueResult(
                LibraryScanQueueOutcome.RootNotFound,
                null,
                "Library root was not found.");
        }

        if (!root.IsEnabled)
        {
            return new LibraryScanQueueResult(
                LibraryScanQueueOutcome.RootDisabled,
                null,
                "Library root is disabled.");
        }

        // Filesystem events prove the mount is live; every other trigger probes first so an
        // offline NAS never produces a failed operation per attempt.
        if (request.Trigger != LibraryScanTrigger.Watch)
        {
            var availability = scope.ServiceProvider.GetRequiredService<LibraryRootAvailabilityService>();
            var storage = await availability.CheckAsync(
                request.RootId,
                force: request.Trigger is LibraryScanTrigger.Manual or LibraryScanTrigger.Retry,
                cancellationToken);
            if (storage is not { IsAvailable: true })
            {
                return new LibraryScanQueueResult(
                    LibraryScanQueueOutcome.RootUnavailable,
                    null,
                    "Library scan was not started because media storage is not currently readable.");
            }
        }

        Reservation reservation;
        lock (gate)
        {
            if (FindConflict(request.RootId, folder) is { } conflict)
            {
                return new LibraryScanQueueResult(
                    LibraryScanQueueOutcome.AlreadyActive,
                    conflict.OperationId == Guid.Empty ? null : conflict.OperationId,
                    "A library scan for this root is already queued or running.");
            }

            reservation = new Reservation(request.RootId, folder);
            active.Add(reservation);
        }

        var scanRequest = request with { Folder = folder };
        var details = new LibraryScanDetails(
            request.RootId,
            folder,
            request.Trigger,
            LibraryScanPhase.Queued,
            0,
            0,
            0,
            null);

        try
        {
            var operationId = await jobs.QueueAsync(
                new OperationDescriptor(
                    OperationKind,
                    OperationCategory,
                    Title(request.Trigger, folder),
                    folder is null ? root.Name : $"{root.Name} · {folder}",
                    request.ProfileId,
                    OperationLane.Maintenance,
                    Retryable: true,
                    Details: details.Serialize()),
                (operation, services, token) => ExecuteAsync(operation, services, scanRequest, token),
                cancellationToken);

            lock (gate)
            {
                reservation.OperationId = operationId;
            }

            return new LibraryScanQueueResult(
                LibraryScanQueueOutcome.Queued,
                operationId,
                folder is null ? "Library scan queued." : $"Scan of {folder} queued.");
        }
        catch
        {
            Release(reservation);
            throw;
        }
    }

    // Queues a fresh run for the root and scope recorded in a finished scan operation.
    // This works after a restart, when the original queued delegate no longer exists.
    public async Task<LibraryScanQueueResult?> RetryAsync(
        Guid operationId,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await new OperationStore(db).GetAsync(operationId, cancellationToken);
        if (operation is null ||
            operation.Kind != OperationKind ||
            LibraryScanDetails.TryParse(operation.Details) is not { } details)
        {
            return null;
        }

        return await QueueAsync(
            new LibraryScanRequest(details.RootId, LibraryScanTrigger.Retry, details.Folder, profileId),
            cancellationToken);
    }

    private async Task ExecuteAsync(
        OperationExecutionContext operation,
        IServiceProvider services,
        LibraryScanRequest request,
        CancellationToken cancellationToken)
    {
        // A retried delegate re-enters here without a reservation; it must not run beside a
        // newer scan of the same root.
        Reservation reservation;
        lock (gate)
        {
            var own = active.FirstOrDefault(x => x.OperationId == operation.OperationId);
            if (own is null)
            {
                if (FindConflict(request.RootId, request.Folder) is not null)
                {
                    throw new LibraryScanException(
                        "Another library scan for this root is already queued or running.");
                }

                own = new Reservation(request.RootId, request.Folder)
                {
                    OperationId = operation.OperationId
                };
                active.Add(own);
            }

            reservation = own;
        }

        try
        {
            await RunAsync(operation, services, request, cancellationToken);
        }
        finally
        {
            Release(reservation);
        }
    }

    private async Task RunAsync(
        OperationExecutionContext operation,
        IServiceProvider services,
        LibraryScanRequest request,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var store = new OperationStore(db);
        var root = await db.LibraryRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.RootId, cancellationToken)
            ?? throw new LibraryScanException("Library root was not found.");
        var rootPath = Path.GetFullPath(root.Path);

        var reporter = new ProgressReporter(
            store,
            operation.OperationId,
            new LibraryScanDetails(
                request.RootId,
                request.Folder,
                request.Trigger,
                LibraryScanPhase.Queued,
                0,
                0,
                0,
                null));

        ScanResult result;
        try
        {
            var scanner = services.GetRequiredService<LibraryScanner>();
            result = request.Folder is null
                ? await scanner.ScanAsync(request.RootId, reporter.ReportAsync, cancellationToken)
                : await scanner.ScanFolderAsync(request.RootId, request.Folder, reporter.ReportAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DirectoryNotFoundException exception)
        {
            logger.LogWarning(
                exception,
                "Library scan skipped unavailable root {RootId}. Existing library state was preserved.",
                request.RootId);
            throw new LibraryScanException("Media root is unavailable.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Library scan cannot access root {RootId}. Existing library state was preserved.",
                request.RootId);
            throw new LibraryScanException("Media root access was denied.", exception);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Library scan could not safely read root {RootId}. Existing library state was preserved.",
                request.RootId);
            throw new LibraryScanException(Sanitize(exception.Message, rootPath, root.Name), exception);
        }
        catch (Exception exception) when (exception is not LibraryScanException)
        {
            logger.LogError(
                exception,
                "Library scan failed for root {RootId}.",
                request.RootId);
            throw new LibraryScanException(
                $"{exception.GetType().Name}: {Sanitize(exception.Message, rootPath, root.Name)}",
                exception);
        }

        foreach (var warning in result.Warnings)
        {
            await store.AppendLogAsync(
                operation.OperationId,
                OperationLogLevel.Warning,
                "Scan",
                $"{warning.Reason}: {warning.RelativePath}",
                cancellationToken);
        }

        if (result.WarningCount > result.Warnings.Count)
        {
            await store.AppendLogAsync(
                operation.OperationId,
                OperationLogLevel.Warning,
                "Scan",
                $"{result.WarningCount - result.Warnings.Count} further warning(s) were not recorded.",
                cancellationToken);
        }

        await reporter.CompleteAsync(result, cancellationToken);

        // New or changed media (for example a fresh download seen by the watcher) need their
        // learning text prepared; the preparation batch itself is idempotent and guarded.
        if (request.Folder is null || result.Discovered > 0 || result.Updated > 0)
        {
            var subtitles = services.GetRequiredService<SubtitleImportService>();
            await subtitles.QueueAllMissingAsync(cancellationToken);
        }

        await store.PruneFinishedAsync(OperationKind, HistoryLimit, CancellationToken.None);
    }

    public static string Summarize(LibraryScanCounters counters) =>
        $"{counters.Discovered} added, {counters.Updated} changed, {counters.Removed} removed, " +
        $"{counters.Skipped} skipped, {counters.Subtitles} subtitle files.";

    public static string PhaseLabel(LibraryScanPhase phase) => phase switch
    {
        LibraryScanPhase.Queued => "Queued",
        LibraryScanPhase.Enumerating => "Enumerating files",
        LibraryScanPhase.Reconciling => "Reconciling media",
        LibraryScanPhase.Metadata => "Matching metadata",
        LibraryScanPhase.Artwork => "Importing artwork",
        LibraryScanPhase.Analyzing => "Analysing media",
        LibraryScanPhase.Subtitles => "Importing subtitles",
        LibraryScanPhase.Completed => "Completed",
        _ => phase.ToString()
    };

    public static string TriggerLabel(LibraryScanTrigger trigger) => trigger switch
    {
        LibraryScanTrigger.Manual => "Manual",
        LibraryScanTrigger.Startup => "Startup",
        LibraryScanTrigger.Watch => "File change",
        LibraryScanTrigger.Periodic => "Periodic",
        LibraryScanTrigger.Retry => "Retry",
        _ => trigger.ToString()
    };

    private static string Title(LibraryScanTrigger trigger, string? folder) => trigger switch
    {
        LibraryScanTrigger.Startup => "Startup library reconciliation",
        LibraryScanTrigger.Watch => "Library change scan",
        LibraryScanTrigger.Periodic => folder is null
            ? "Periodic library reconciliation"
            : "Library change scan",
        LibraryScanTrigger.Retry => "Library scan retry",
        _ => "Library scan"
    };

    // Operation records never carry the host path of a root; the root name replaces it.
    private static string Sanitize(string message, string rootPath, string rootName)
    {
        var trimmedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return message
            .Replace(rootPath, $"[{rootName}]", StringComparison.Ordinal)
            .Replace(trimmedRoot, $"[{rootName}]", StringComparison.Ordinal);
    }

    private static string? NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var normalized = folder.Trim()
            .Replace('\\', '/')
            .Trim('/');
        if (normalized.Length == 0 ||
            normalized == "." ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException(
                "The folder to scan must be a relative path inside the library root.",
                nameof(folder));
        }

        return normalized;
    }

    // An active full scan absorbs every request for its root; a folder scan only blocks the
    // same folder. A full scan requested beside active folder scans simply runs after them.
    private Reservation? FindConflict(Guid rootId, string? folder) =>
        active.FirstOrDefault(x =>
            x.RootId == rootId &&
            (x.Folder is null ||
             (folder is not null && string.Equals(x.Folder, folder, StringComparison.Ordinal))));

    private void Release(Reservation reservation)
    {
        lock (gate)
        {
            active.Remove(reservation);
        }
    }

    private sealed class Reservation(Guid rootId, string? folder)
    {
        public Guid RootId { get; } = rootId;
        public string? Folder { get; } = folder;
        public Guid OperationId { get; set; }
    }

    // Persists phase and counters at a bounded rate: phase changes always, file progress at
    // most once per second.
    private sealed class ProgressReporter(
        OperationStore store,
        Guid operationId,
        LibraryScanDetails initial)
    {
        private LibraryScanDetails details = initial;
        private DateTime lastWriteUtc = DateTime.MinValue;

        public async Task ReportAsync(
            LibraryScanProgress progress,
            CancellationToken cancellationToken)
        {
            var phaseChanged = progress.Phase != details.Phase;
            details = details with
            {
                Phase = progress.Phase,
                FilesProcessed = progress.Phase == LibraryScanPhase.Reconciling
                    ? progress.Processed
                    : details.FilesProcessed,
                FilesTotal = progress.Phase == LibraryScanPhase.Reconciling
                    ? progress.Total
                    : details.FilesTotal
            };

            var now = DateTime.UtcNow;
            if (!phaseChanged && now - lastWriteUtc < ProgressWriteInterval)
            {
                return;
            }

            lastWriteUtc = now;
            await store.ReportProgressAsync(
                operationId,
                Percent(progress),
                Message(progress),
                cancellationToken: cancellationToken);
            await store.SetDetailsAsync(operationId, details.Serialize(), cancellationToken);
        }

        public async Task CompleteAsync(ScanResult result, CancellationToken cancellationToken)
        {
            var counters = new LibraryScanCounters(
                result.MediaFiles,
                result.Discovered,
                result.Updated,
                result.Removed,
                result.Skipped,
                result.SubtitleFiles,
                result.ArtworkImported,
                result.MetadataWarnings,
                result.Errors,
                result.MediaInventory.Analyzed,
                result.MediaInventory.Failed);

            details = details with
            {
                Phase = LibraryScanPhase.Completed,
                FilesProcessed = result.MediaFiles,
                FilesTotal = result.MediaFiles,
                Warnings = result.WarningCount,
                Counters = counters
            };

            await store.ReportProgressAsync(
                operationId,
                100,
                Summarize(counters),
                cancellationToken: cancellationToken);
            await store.SetDetailsAsync(operationId, details.Serialize(), cancellationToken);
        }

        private static int Percent(LibraryScanProgress progress)
        {
            static int Span(int from, int to, int processed, int total) =>
                total <= 0 ? from : from + (int)((long)(to - from) * Math.Min(processed, total) / total);

            return progress.Phase switch
            {
                LibraryScanPhase.Queued => 0,
                LibraryScanPhase.Enumerating => 2,
                LibraryScanPhase.Reconciling => Span(5, 60, progress.Processed, progress.Total),
                LibraryScanPhase.Metadata => Span(60, 70, progress.Processed, progress.Total),
                LibraryScanPhase.Artwork => Span(70, 78, progress.Processed, progress.Total),
                LibraryScanPhase.Analyzing => 79,
                LibraryScanPhase.Subtitles => Span(80, 98, progress.Processed, progress.Total),
                _ => 100
            };
        }

        private static string Message(LibraryScanProgress progress) =>
            progress.Total > 0 && progress.Phase != LibraryScanPhase.Completed
                ? $"{PhaseLabel(progress.Phase)} ({Math.Min(progress.Processed, progress.Total)} of {progress.Total})."
                : $"{PhaseLabel(progress.Phase)}.";
    }
}
