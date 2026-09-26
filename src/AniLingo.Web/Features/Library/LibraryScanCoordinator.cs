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
// Folders is null for a whole-root reconciliation, otherwise the root-relative folders that
// the run reconciles (several watcher changes of one root are merged into one run).
public sealed record LibraryScanDetails(
    Guid RootId,
    IReadOnlyList<string>? Folders,
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
    // A new run was queued.
    Queued = 1,

    // A run of the root is already running; nothing was queued.
    AlreadyActive = 2,
    RootUnavailable = 3,
    RootNotFound = 4,
    RootDisabled = 5,

    // The request was merged into the root's run that is queued but not yet running.
    Merged = 6
}

public sealed record LibraryScanQueueResult(
    LibraryScanQueueOutcome Outcome,
    Guid? OperationId,
    string Message)
{
    // True when a queued run will serve the request (new or merged).
    public bool Queued => Outcome is LibraryScanQueueOutcome.Queued or LibraryScanQueueOutcome.Merged;
}

public sealed record ActiveLibraryScan(
    Guid RootId,
    IReadOnlyList<string>? Folders,
    Guid OperationId,
    bool IsRunning);

// Surfaces a scan failure with a message that is safe to persist in the operation record.
public sealed class LibraryScanException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// One entry point for every library scan: startup, manual, watcher and periodic runs share
// one guard per root, run on the maintenance lane and are recorded as Operations.
//
// Coalescing: a root has at most one active run. A request for a root whose run is queued
// but not yet running is merged into it (folders are added; a whole-root request widens the
// run to the whole root). While a run is executing, further requests are rejected with
// AlreadyActive; the watcher keeps such changes pending and offers them again afterwards.
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
    private readonly Dictionary<Guid, Reservation> active = new();

    public IReadOnlyList<ActiveLibraryScan> GetActive(Guid rootId)
    {
        lock (gate)
        {
            return active.TryGetValue(rootId, out var reservation)
                ? [new ActiveLibraryScan(rootId, reservation.Folders, reservation.OperationId, reservation.Started)]
                : [];
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
        var created = false;
        string? mergedDetails = null;
        lock (gate)
        {
            if (active.TryGetValue(request.RootId, out var existing))
            {
                if (existing.Started)
                {
                    return new LibraryScanQueueResult(
                        LibraryScanQueueOutcome.AlreadyActive,
                        existing.OperationId,
                        "A library scan for this root is already running.");
                }

                if (existing.Merge(folder) && existing.OperationId != Guid.Empty)
                {
                    mergedDetails = existing.QueuedDetails().Serialize();
                }

                reservation = existing;
            }
            else
            {
                reservation = new Reservation(request.RootId, request.Trigger, folder);
                active.Add(request.RootId, reservation);
                created = true;
            }
        }

        if (created)
        {
            return await CreateRunAsync(reservation, request, root.Name, db, cancellationToken);
        }

        // Merged into the queued run; keep its persisted scope current for the UI.
        if (mergedDetails is not null)
        {
            await new OperationStore(db).SetDetailsAsync(
                reservation.OperationId,
                mergedDetails,
                cancellationToken);
        }

        return new LibraryScanQueueResult(
            LibraryScanQueueOutcome.Merged,
            reservation.OperationId == Guid.Empty ? null : reservation.OperationId,
            "A library scan for this root is already queued; the request was added to it.");
    }

    // Queues a fresh run for the root and scope recorded in a finished scan operation.
    // This works after a restart, when the original queued delegate no longer exists.
    public async Task<LibraryScanQueueResult?> RetryAsync(
        Guid operationId,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        LibraryScanDetails? details;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operation = await new OperationStore(db).GetAsync(operationId, cancellationToken);
            details = operation is { Kind: OperationKind }
                ? LibraryScanDetails.TryParse(operation.Details)
                : null;
        }

        if (details is null)
        {
            return null;
        }

        if (details.Folders is not { Count: > 0 } folders)
        {
            return await QueueAsync(
                new LibraryScanRequest(details.RootId, LibraryScanTrigger.Retry, null, profileId),
                cancellationToken);
        }

        // The first folder creates the run; the others are merged into it.
        LibraryScanQueueResult? first = null;
        foreach (var folder in folders)
        {
            var result = await QueueAsync(
                new LibraryScanRequest(details.RootId, LibraryScanTrigger.Retry, folder, profileId),
                cancellationToken);
            first ??= result;
        }

        return first;
    }

    private async Task<LibraryScanQueueResult> CreateRunAsync(
        Reservation reservation,
        LibraryScanRequest request,
        string rootName,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        int queuedVersion;
        string details;
        lock (gate)
        {
            queuedVersion = reservation.Version;
            details = reservation.QueuedDetails().Serialize();
        }

        Guid operationId;
        try
        {
            operationId = await jobs.QueueAsync(
                new OperationDescriptor(
                    OperationKind,
                    OperationCategory,
                    Title(request.Trigger),
                    rootName,
                    request.ProfileId,
                    OperationLane.Maintenance,
                    Retryable: true,
                    Details: details),
                (operation, services, token) => ExecuteAsync(operation, services, request, token),
                cancellationToken);
        }
        catch
        {
            Release(reservation);
            throw;
        }

        string? staleDetails = null;
        lock (gate)
        {
            reservation.OperationId = operationId;
            if (reservation.Version != queuedVersion && !reservation.Started)
            {
                // Requests were merged while the operation record was being created.
                staleDetails = reservation.QueuedDetails().Serialize();
            }
        }

        if (staleDetails is not null)
        {
            await new OperationStore(db).SetDetailsAsync(operationId, staleDetails, cancellationToken);
        }

        return new LibraryScanQueueResult(
            LibraryScanQueueOutcome.Queued,
            operationId,
            request.Folder is null ? "Library scan queued." : $"Scan of {request.Folder} queued.");
    }

    private async Task ExecuteAsync(
        OperationExecutionContext operation,
        IServiceProvider services,
        LibraryScanRequest request,
        CancellationToken cancellationToken)
    {
        Reservation? reservation = null;
        IReadOnlyList<string>? folders = null;
        lock (gate)
        {
            if (active.TryGetValue(request.RootId, out var existing))
            {
                if (existing.OperationId != operation.OperationId)
                {
                    // A retried delegate must not run beside a newer run of the same root.
                    throw new LibraryScanException(
                        "Another library scan for this root is already queued or running.");
                }

                existing.Started = true;
                folders = existing.Folders;
                reservation = existing;
            }
        }

        try
        {
            if (reservation is null)
            {
                // A generic retry of a finished run: its scope is the persisted one.
                var persisted = await new OperationStore(services.GetRequiredService<AppDbContext>())
                    .GetAsync(operation.OperationId, cancellationToken);
                var persistedFolders = LibraryScanDetails.TryParse(persisted?.Details)?.Folders;
                lock (gate)
                {
                    if (active.ContainsKey(request.RootId))
                    {
                        throw new LibraryScanException(
                            "Another library scan for this root is already queued or running.");
                    }

                    reservation = new Reservation(request.RootId, request.Trigger, persistedFolders)
                    {
                        OperationId = operation.OperationId,
                        Started = true
                    };
                    active.Add(request.RootId, reservation);
                    folders = reservation.Folders;
                }
            }

            await RunAsync(operation, services, request, folders, cancellationToken);
        }
        finally
        {
            if (reservation is not null)
            {
                Release(reservation);
            }
        }
    }

    private async Task RunAsync(
        OperationExecutionContext operation,
        IServiceProvider services,
        LibraryScanRequest request,
        IReadOnlyList<string>? folders,
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
                folders,
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
            if (folders is null)
            {
                result = await scanner.ScanAsync(request.RootId, reporter.ReportAsync, cancellationToken);
            }
            else
            {
                // Each folder goes through the exact full-scan reconciliation, scoped to it.
                var results = new List<ScanResult>(folders.Count);
                for (var index = 0; index < folders.Count; index++)
                {
                    reporter.BeginSegment(index, folders.Count);
                    results.Add(await scanner.ScanFolderAsync(
                        request.RootId,
                        folders[index],
                        reporter.ReportAsync,
                        cancellationToken));
                }

                result = ScanResult.Combine(results);
            }
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
        if (folders is null || result.Discovered > 0 || result.Updated > 0)
        {
            var subtitles = services.GetRequiredService<SubtitleImportService>();
            await subtitles.QueueAllMissingAsync(cancellationToken);
        }

        await store.PruneFinishedAsync(OperationKind, HistoryLimit, CancellationToken.None);
    }

    public static string Summarize(LibraryScanCounters counters) =>
        $"{counters.Discovered} added, {counters.Updated} changed, {counters.Removed} removed, " +
        $"{counters.Skipped} skipped, {counters.Subtitles} subtitle files.";

    public static string DescribeScope(IReadOnlyList<string>? folders) =>
        folders is not { Count: > 0 } ? "Whole root" : string.Join(", ", folders);

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

    private static string Title(LibraryScanTrigger trigger) => trigger switch
    {
        LibraryScanTrigger.Startup => "Startup library reconciliation",
        LibraryScanTrigger.Watch => "Library change scan",
        LibraryScanTrigger.Periodic => "Periodic library reconciliation",
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

    private void Release(Reservation reservation)
    {
        lock (gate)
        {
            if (active.TryGetValue(reservation.RootId, out var current) &&
                ReferenceEquals(current, reservation))
            {
                active.Remove(reservation.RootId);
            }
        }
    }

    // The single active run of a root. Mutated only under the coordinator gate.
    private sealed class Reservation
    {
        // Null means the whole root.
        private SortedSet<string>? folders;

        public Reservation(Guid rootId, LibraryScanTrigger trigger, string? folder)
            : this(rootId, trigger, folder is null ? null : new[] { folder })
        {
        }

        public Reservation(Guid rootId, LibraryScanTrigger trigger, IReadOnlyList<string>? scope)
        {
            RootId = rootId;
            Trigger = trigger;
            folders = scope is { Count: > 0 }
                ? new SortedSet<string>(scope, StringComparer.Ordinal)
                : null;
        }

        public Guid RootId { get; }
        public LibraryScanTrigger Trigger { get; }
        public Guid OperationId { get; set; }
        public bool Started { get; set; }

        // Bumped whenever the scope widens, so a merge during operation creation is persisted.
        public int Version { get; private set; }

        public IReadOnlyList<string>? Folders => folders?.ToArray();

        // Widens the scope to include the folder (null: the whole root); returns whether it changed.
        public bool Merge(string? folder)
        {
            if (folders is null)
            {
                return false;
            }

            if (folder is null)
            {
                folders = null;
            }
            else if (!folders.Add(folder))
            {
                return false;
            }

            Version++;
            return true;
        }

        public LibraryScanDetails QueuedDetails() =>
            new(RootId, Folders, Trigger, LibraryScanPhase.Queued, 0, 0, 0, null);
    }

    // Persists phase and counters at a bounded rate: phase changes always, file progress at
    // most once per second. A multi-folder run reports each folder as one segment of 0-100 %.
    private sealed class ProgressReporter(
        OperationStore store,
        Guid operationId,
        LibraryScanDetails initial)
    {
        private LibraryScanDetails details = initial;
        private DateTime lastWriteUtc = DateTime.MinValue;
        private int segment;
        private int segments = 1;

        public void BeginSegment(int index, int count)
        {
            segment = index;
            segments = Math.Max(1, count);
            details = details with { Phase = LibraryScanPhase.Queued };
        }

        public async Task ReportAsync(
            LibraryScanProgress progress,
            CancellationToken cancellationToken)
        {
            // The final state is written once by CompleteAsync, also for multi-folder runs.
            if (progress.Phase == LibraryScanPhase.Completed)
            {
                return;
            }

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
                (segment * 100 + Percent(progress)) / segments,
                segments > 1
                    ? $"Folder {segment + 1} of {segments}: {Message(progress)}"
                    : Message(progress),
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
            progress.Total > 0
                ? $"{PhaseLabel(progress.Phase)} ({Math.Min(progress.Processed, progress.Total)} of {progress.Total})."
                : $"{PhaseLabel(progress.Phase)}.";
    }
}
