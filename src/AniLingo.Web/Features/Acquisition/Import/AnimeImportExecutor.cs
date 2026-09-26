using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Pipeline;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Sonarr;

namespace AniLingo.Web.Features.Acquisition.Import;

public sealed record AnimeImportActionResult(
    bool Success,
    string Message);

/// <summary>
/// Executes the completed-download import for anime: plans with the canonical planner, consults
/// Sonarr ownership before touching any path, moves accepted files into the anime's library
/// folder, preserves existing files until the replacement committed, then reconciles that anime
/// folder through the library scanner. Uncertain or failed files become manual-intervention
/// records the owner resolves on the acquisition overview.
/// </summary>
public sealed class AnimeImportExecutor(
    AppDbContext db,
    AnimeImportStore imports,
    SabnzbdAcquisitionStore acquisitions,
    AcquisitionOwnershipStore ownershipStore,
    SonarrObservationService observation,
    AnimeAcquisitionInventory inventory,
    LibraryScanner scanner,
    ISabnzbdClient sabnzbd,
    SabnzbdConnectionResolver connections,
    ILogger<AnimeImportExecutor> logger)
{
    public const string OperationKind = "anime-import";
    public const string OperationCategory = "Acquisition";
    public const string LogModule = "Import";
    public static readonly TimeSpan RecoveryWindow = TimeSpan.FromDays(7);

    // The SABnzbd monitor, startup recovery and owner actions run in different scopes; one
    // process-wide gate keeps two of them from executing the same import at the same time.
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    public static bool IsAnimeDownload(OperationSnapshot operation) =>
        string.Equals(operation.Kind, SabnzbdAcquisitionService.OperationKind, StringComparison.Ordinal);

    /// <summary>
    /// Imports the files of a completed anime SABnzbd job. Idempotent per download operation:
    /// a finished record is returned as is, an interrupted one is resumed.
    /// </summary>
    public async Task<AnimeImportRecord?> ImportCompletedAsync(
        OperationSnapshot download,
        string? storagePath,
        CancellationToken cancellationToken)
    {
        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            return await ImportCompletedCoreAsync(download, storagePath, cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<AnimeImportRecord?> ImportCompletedCoreAsync(
        OperationSnapshot download,
        string? storagePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (!IsAnimeDownload(download))
        {
            return null;
        }

        var existing = await imports.FindByDownloadAsync(download.Id, cancellationToken);
        if (existing is not null && existing.Status != AnimeImportStatus.Importing)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var acquisition = (await acquisitions.FindByOperationAsync(download.Id, cancellationToken))?.Acquisition;
        var record = existing ?? new AnimeImportRecord(
            Guid.NewGuid(),
            download.Id,
            null,
            acquisition?.Id,
            acquisition?.AnimeKey ?? "",
            acquisition?.AnimeTitle ?? download.Subject ?? "Anime",
            storagePath ?? existing?.DownloadPath,
            AnimeImportStatus.Importing,
            [],
            null,
            now,
            now);

        if (acquisition is null)
        {
            return await FinishAsync(
                record with { AnimeKey = record.AnimeKey.Length == 0 ? "unknown" : record.AnimeKey },
                AnimeImportStatus.Failed,
                "No acquisition relation exists for this download, so its episodes are unknown. Import it manually.",
                null,
                cancellationToken);
        }

        var operations = new OperationStore(db);
        var operationId = record.ImportOperationId ?? await operations.CreateAsync(
            new OperationDescriptor(
                OperationKind,
                OperationCategory,
                "Anime import",
                $"{acquisition.AnimeTitle} · {SabnzbdAcquisitionService.FormatEpisodes(acquisition.Episodes)}",
                acquisition.ProfileId,
                OperationLane.Normal,
                Retryable: false),
            cancellationToken);
        if (record.ImportOperationId is null)
        {
            await operations.MarkRunningAsync(operationId, cancellationToken);
        }

        record = record with
        {
            ImportOperationId = operationId,
            DownloadPath = storagePath ?? record.DownloadPath,
            UpdatedAtUtc = now
        };
        await imports.UpsertAsync(record, cancellationToken);

        try
        {
            return await PlanAndExecuteAsync(record, acquisition, download, operationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Anime import for download {OperationId} failed.", download.Id);
            return await FinishAsync(
                record,
                AnimeImportStatus.Failed,
                $"Import failed: {exception.Message}",
                operationId,
                cancellationToken);
        }
    }

    /// <summary>
    /// Owner decision for a file the planner did not import automatically: import it as the
    /// given local episode. Ownership rules still apply; only the confidence gate is bypassed.
    /// </summary>
    public async Task<AnimeImportActionResult> ImportManuallyAsync(
        Guid recordId,
        string sourcePath,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            return await ImportManuallyCoreAsync(recordId, sourcePath, seasonNumber, episodeNumber, cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<AnimeImportActionResult> ImportManuallyCoreAsync(
        Guid recordId,
        string sourcePath,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        var record = await imports.GetAsync(recordId, cancellationToken);
        if (record is null)
        {
            return new(false, "Import record not found.");
        }

        var index = Array.FindIndex(record.Files, file =>
            file.SourcePath.Equals(sourcePath, StringComparison.Ordinal) &&
            file.Status is AnimeImportFileStatus.ManualRequired or AnimeImportFileStatus.Failed or AnimeImportFileStatus.Ignored);
        if (index < 0)
        {
            return new(false, "This file is not waiting for a manual import.");
        }

        if (seasonNumber < 0 || episodeNumber <= 0)
        {
            return new(false, "Choose a valid season and episode.");
        }

        var target = await inventory.LoadAsync(record.AnimeKey, cancellationToken);
        if (target is null)
        {
            return new(false, "The anime no longer exists in the library.");
        }

        if (!File.Exists(sourcePath))
        {
            return new(false, "The downloaded file no longer exists.");
        }

        var snapshot = await observation.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        var jobId = record.AcquisitionId?.ToString() ?? record.Id.ToString();
        var download = await new OperationStore(db).GetAsync(record.DownloadOperationId, cancellationToken);
        var allowed = SonarrParallelSafety.CanImport(
            snapshot,
            new AcquisitionImportRequest(record.AnimeKey, jobId, download?.ExternalId, sourcePath));
        if (!allowed.Allowed)
        {
            return new(false, $"Ownership: {allowed.Reason}");
        }

        var slot = target.Find(seasonNumber, episodeNumber);
        var requested = new RequestedAnimeEpisode(seasonNumber, episodeNumber, slot?.Key.AbsoluteEpisodeNumber);
        var replaced = new List<string>();
        if (slot?.FilePath is { } currentPath)
        {
            var mutation = SonarrParallelSafety.CanMutateLibraryPath(snapshot, record.AnimeKey, currentPath);
            if (!mutation.Allowed)
            {
                return new(false, $"The existing file for S{seasonNumber:00}E{episodeNumber:00} cannot be replaced. {mutation.Reason}");
            }

            replaced.Add(currentPath);
        }

        var file = record.Files[index];
        var planned = new PlannedAnimeImport(
            new CompletedDownloadFile(file.SourcePath, file.SizeBytes),
            AnimeImportDisposition.AutoImport,
            AnimeImportFileAction.Move,
            [requested],
            file.SidecarPaths.Where(File.Exists).ToArray(),
            replaced,
            1,
            ["Imported manually by the owner."]);

        var location = await inventory.GetLibraryLocationAsync(target.Anime.Id, cancellationToken);
        var operationId = record.ImportOperationId;
        var executed = await ExecuteFileAsync(planned, target, location, snapshot, jobId, operationId, cancellationToken);

        var files = record.Files.ToArray();
        files[index] = executed;
        var reconciled = executed.Status == AnimeImportFileStatus.Imported && location is not null
            ? await ReconcileAsync(location, operationId, cancellationToken)
            : null;

        var status = files.Any(item => item.Status is AnimeImportFileStatus.ManualRequired or AnimeImportFileStatus.Failed)
            ? AnimeImportStatus.ManualRequired
            : AnimeImportStatus.Imported;
        await FinishAsync(
            record with { Files = files },
            status,
            executed.Status == AnimeImportFileStatus.Imported
                ? $"Imported {Path.GetFileName(executed.ImportedPath)} manually as S{seasonNumber:00}E{episodeNumber:00}.{reconciled}"
                : executed.Error ?? "Manual import did not complete.",
            operationId,
            cancellationToken,
            finalizeOperation: status != AnimeImportStatus.ManualRequired);

        return executed.Status == AnimeImportFileStatus.Imported
            ? new(true, $"Imported as S{seasonNumber:00}E{episodeNumber:00}.")
            : new(false, executed.Error ?? "Manual import did not complete.");
    }

    public async Task<AnimeImportActionResult> DismissAsync(
        Guid recordId,
        CancellationToken cancellationToken)
    {
        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var record = await imports.GetAsync(recordId, cancellationToken);
            if (record is null)
            {
                return new(false, "Import record not found.");
            }

            if (!record.NeedsAttention)
            {
                return new(false, "Only imports waiting for attention can be dismissed.");
            }

            await FinishAsync(
                record,
                AnimeImportStatus.Dismissed,
                "Dismissed by the owner; the downloaded files were left untouched.",
                record.ImportOperationId,
                cancellationToken);
            return new(true, "Import dismissed.");
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    /// <summary>
    /// Restart recovery: resumes imports interrupted mid-way and imports anime downloads that
    /// completed while AniLingo was not running (their storage path is read from SABnzbd history).
    /// </summary>
    public async Task<int> RecoverAsync(CancellationToken cancellationToken)
    {
        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            return await RecoverCoreAsync(cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<int> RecoverCoreAsync(CancellationToken cancellationToken)
    {
        var recovered = 0;
        var operations = new OperationStore(db);
        var state = await imports.LoadAsync(cancellationToken);

        foreach (var record in state.Imports.Where(record => record.Status == AnimeImportStatus.Importing).ToArray())
        {
            var download = await operations.GetAsync(record.DownloadOperationId, cancellationToken);
            if (download is null)
            {
                await FinishAsync(record, AnimeImportStatus.Failed, "The download operation no longer exists.", record.ImportOperationId, cancellationToken);
                continue;
            }

            await ImportCompletedCoreAsync(download, record.DownloadPath, cancellationToken);
            recovered++;
        }

        var known = state.Imports.Select(record => record.DownloadOperationId).ToHashSet();
        var since = DateTime.UtcNow - RecoveryWindow;
        var completed = (await operations.ListAsync(
                new OperationListFilter(View: "history", Category: SabnzbdDownloadService.OperationCategory, Limit: 200),
                cancellationToken))
            .Where(operation =>
                IsAnimeDownload(operation) &&
                operation.Status == OperationStatus.Succeeded &&
                !string.IsNullOrWhiteSpace(operation.ExternalId) &&
                operation.FinishedAtUtc is { } finished && finished >= since &&
                !known.Contains(operation.Id))
            .ToArray();
        if (completed.Length == 0)
        {
            return recovered;
        }

        var connection = await connections.GetConnectionAsync(cancellationToken);
        if (connection is null)
        {
            logger.LogWarning(
                "{Count} completed anime downloads await import but SABnzbd is not configured.",
                completed.Length);
            return recovered;
        }

        var history = await sabnzbd.GetHistoryAsync(
            connection,
            completed.Select(operation => operation.ExternalId!).ToArray(),
            cancellationToken);
        foreach (var operation in completed)
        {
            var job = history.Jobs.FirstOrDefault(item => item.NzoId == operation.ExternalId);
            await ImportCompletedCoreAsync(operation, job?.StoragePath, cancellationToken);
            recovered++;
        }

        return recovered;
    }

    private async Task<AnimeImportRecord> PlanAndExecuteAsync(
        AnimeImportRecord record,
        SabnzbdAcquisition acquisition,
        OperationSnapshot download,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var target = await inventory.LoadAsync(acquisition.AnimeKey, cancellationToken);
        if (target is null)
        {
            return await FinishAsync(record, AnimeImportStatus.Failed, "The anime no longer exists in the library.", operationId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(record.DownloadPath))
        {
            return await FinishAsync(record, AnimeImportStatus.Failed, "SABnzbd reported no storage path for the completed job.", operationId, cancellationToken);
        }

        var files = EnumerateDownload(record.DownloadPath, out var enumerationError);
        if (enumerationError is not null)
        {
            return await FinishAsync(record, AnimeImportStatus.Failed, enumerationError, operationId, cancellationToken);
        }

        var requested = acquisition.Episodes
            .Select(key => new RequestedAnimeEpisode(
                key.SeasonNumber,
                key.EpisodeNumber,
                key.AbsoluteEpisodeNumber ?? target.Find(key.SeasonNumber, key.EpisodeNumber)?.Key.AbsoluteEpisodeNumber))
            .ToArray();
        if (requested.Length == 0)
        {
            return await FinishAsync(record, AnimeImportStatus.Failed, "The acquisition names no episodes.", operationId, cancellationToken);
        }

        var aliases = requested
            .SelectMany(episode => target.Find(episode.SeasonNumber, episode.EpisodeNumber) is { } slot
                ? new[] { slot.SearchTitle }.Concat(slot.SearchAliases)
                : [])
            .Append(target.Anime.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing = target.Episodes
            .Where(episode => episode.HasFile)
            .Select(episode => new ExistingAnimeFile(
                episode.FilePath!,
                episode.Key.SeasonNumber,
                episode.Key.EpisodeNumber,
                episode.FileSizeBytes ?? 0))
            .ToArray();

        var snapshot = await observation.GetSnapshotAsync(forceRefresh: true, cancellationToken);
        var jobId = acquisition.Id.ToString();
        var plan = CompletedDownloadImportPlanner.Plan(
            new CompletedDownloadImportContext(
                jobId,
                acquisition.AnimeKey,
                aliases,
                requested,
                target.Profile,
                AnimeImportFileAction.Move,
                DownloadId: download.ExternalId),
            files,
            existing,
            snapshot);

        var operations = new OperationStore(db);
        if (plan.BlockedByOwnership)
        {
            await operations.AppendLogAsync(operationId, OperationLogLevel.Warning, LogModule, plan.OwnershipBlockReason!, cancellationToken);
            return await FinishAsync(
                record with { Files = plan.Files.Select(file => ToRecord(file, AnimeImportFileStatus.Ignored, null, null)).ToArray() },
                AnimeImportStatus.Failed,
                plan.OwnershipBlockReason!,
                operationId,
                cancellationToken);
        }

        if (plan.Files.Count == 0)
        {
            return await FinishAsync(record, AnimeImportStatus.Failed, "The completed download contains no files.", operationId, cancellationToken);
        }

        await ownershipStore.UpdateAsync(
            state => state.Jobs.TryGetValue(jobId, out var job)
                ? SonarrParallelSafety.RegisterJob(state, job with { Status = AcquisitionOwnershipStatus.Importing, UpdatedAtUtc = DateTimeOffset.UtcNow })
                : state,
            cancellationToken);

        var location = await inventory.GetLibraryLocationAsync(target.Anime.Id, cancellationToken);
        var results = new List<AnimeImportFileRecord>();
        foreach (var planned in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(planned.Source.Path);
            switch (planned.Disposition)
            {
                case AnimeImportDisposition.AutoImport:
                    results.Add(await ExecuteFileAsync(planned, target, location, snapshot, jobId, operationId, cancellationToken));
                    break;

                case AnimeImportDisposition.ManualReview:
                    await operations.AppendLogAsync(operationId, OperationLogLevel.Warning, LogModule, $"Manual review: {name} — {string.Join(" ", planned.Reasons)}", cancellationToken);
                    results.Add(ToRecord(planned, AnimeImportFileStatus.ManualRequired, null, null));
                    break;

                default:
                    if (planned.Targets.Count > 0 || !IsSidecarOrJunk(planned.Source.Path))
                    {
                        await operations.AppendLogAsync(operationId, OperationLogLevel.Information, LogModule, $"Ignored: {name} — {string.Join(" ", planned.Reasons)}", cancellationToken);
                    }

                    results.Add(ToRecord(planned, AnimeImportFileStatus.Ignored, null, null));
                    break;
            }
        }

        var imported = results.Count(result => result.Status == AnimeImportFileStatus.Imported);
        var reconciled = imported > 0 && location is not null
            ? await ReconcileAsync(location, operationId, cancellationToken)
            : null;

        var attention = results.Any(result => result.Status is AnimeImportFileStatus.ManualRequired or AnimeImportFileStatus.Failed);
        var status = attention
            ? AnimeImportStatus.ManualRequired
            : imported > 0
                ? AnimeImportStatus.Imported
                : AnimeImportStatus.Failed;
        var message = status switch
        {
            AnimeImportStatus.Imported => $"Imported {imported} file(s) into the library.{reconciled}",
            AnimeImportStatus.ManualRequired => imported > 0
                ? $"Imported {imported} file(s); {results.Count - imported} need(s) a manual decision.{reconciled}"
                : "The download needs a manual import decision.",
            _ => "No video file could be imported."
        };

        return await FinishAsync(
            record with { Files = results.ToArray() },
            status,
            message,
            operationId,
            cancellationToken,
            finalizeOperation: status != AnimeImportStatus.ManualRequired);
    }

    private async Task<AnimeImportFileRecord> ExecuteFileAsync(
        PlannedAnimeImport planned,
        AnimeAcquisitionTarget target,
        AnimeLibraryLocation? location,
        AcquisitionOwnershipSnapshot snapshot,
        string jobId,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        var operations = new OperationStore(db);
        var name = Path.GetFileName(planned.Source.Path);

        async Task<AnimeImportFileRecord> ManualAsync(string reason)
        {
            if (operationId is { } id)
            {
                await operations.AppendLogAsync(id, OperationLogLevel.Warning, LogModule, $"Manual review: {name} — {reason}", cancellationToken);
            }

            return ToRecord(planned, AnimeImportFileStatus.ManualRequired, null, reason);
        }

        if (location?.AnimeDirectory is null)
        {
            return await ManualAsync("The anime has no folder in an enabled library root yet; create it and rescan, then import manually.");
        }

        if (!File.Exists(planned.Source.Path))
        {
            return await ManualAsync("The downloaded file no longer exists.");
        }

        var directory = AnimeImportDestination.ResolveDirectory(location, planned.Targets[0].SeasonNumber);
        var destination = Path.Combine(
            directory,
            AnimeImportDestination.BuildFileName(target.Anime.Title, planned.Targets, name));
        if (!Path.GetFullPath(destination).StartsWith(
                Path.GetFullPath(location.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return await ManualAsync("The destination would leave the library root.");
        }

        if (File.Exists(destination) &&
            !planned.ExistingPathsToReplaceAfterCommit.Any(path => SonarrOwnershipRecognizer.PathEquals(path, destination)))
        {
            return await ManualAsync($"Destination already exists: {Path.GetFileName(destination)}.");
        }

        // Claim the destination for this job first so parallel mode can mutate it, then verify
        // Sonarr does not act on it. The claim is withdrawn when the check fails.
        var state = await ownershipStore.UpdateAsync(
            current => current.Paths.ContainsKey(SonarrParallelSafety.NormalizePath(destination))
                ? current
                : SonarrParallelSafety.RegisterPath(
                    current,
                    new ManagedMediaPath(destination, target.Anime.Key, AcquisitionOwner.AniLingo, jobId, DateTimeOffset.UtcNow)),
            cancellationToken);
        var mutation = SonarrParallelSafety.CanMutateLibraryPath(snapshot with { State = state }, target.Anime.Key, destination);
        if (!mutation.Allowed)
        {
            await ReleasePathAsync(destination, jobId, cancellationToken);
            return await ManualAsync($"Ownership: {mutation.Reason}");
        }

        try
        {
            Directory.CreateDirectory(directory);
            if (planned.FileAction == AnimeImportFileAction.Copy)
            {
                File.Copy(planned.Source.Path, destination, overwrite: false);
            }
            else
            {
                File.Move(planned.Source.Path, destination, overwrite: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ReleasePathAsync(destination, jobId, cancellationToken);
            var error = $"The library folder is not writable or the file could not be moved: {exception.Message}";
            if (operationId is { } failedId)
            {
                await operations.AppendLogAsync(failedId, OperationLogLevel.Error, LogModule, $"Failed: {name} — {error}", cancellationToken);
            }

            return ToRecord(planned, AnimeImportFileStatus.Failed, null, error);
        }

        var notes = new List<string>();
        foreach (var sidecar in planned.SidecarPaths)
        {
            var sidecarDestination = Path.Combine(
                directory,
                AnimeImportDestination.BuildSidecarName(name, Path.GetFileName(sidecar), Path.GetFileName(destination)));
            try
            {
                if (File.Exists(sidecar) && !File.Exists(sidecarDestination))
                {
                    File.Move(sidecar, sidecarDestination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Sidecar {Path.GetFileName(sidecar)} stayed in the download folder: {exception.Message}");
            }
        }

        foreach (var replaced in planned.ExistingPathsToReplaceAfterCommit)
        {
            if (SonarrOwnershipRecognizer.PathEquals(replaced, destination))
            {
                continue;
            }

            try
            {
                File.Delete(replaced);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notes.Add($"Replaced file {Path.GetFileName(replaced)} could not be deleted: {exception.Message}");
            }
        }

        if (operationId is { } importedId)
        {
            var targets = string.Join(", ", planned.Targets.Select(item => $"S{item.SeasonNumber:00}E{item.EpisodeNumber:00}"));
            await operations.AppendLogAsync(
                importedId,
                OperationLogLevel.Information,
                LogModule,
                $"Imported {name} as {targets} → {Path.GetFileName(destination)} (confidence {planned.Confidence:0.00}). {string.Join(" ", planned.Reasons.Concat(notes))}",
                cancellationToken);
        }

        return ToRecord(planned, AnimeImportFileStatus.Imported, destination, notes.Count == 0 ? null : string.Join(" ", notes));
    }

    private async Task<string> ReconcileAsync(
        AnimeLibraryLocation location,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await scanner.ScanAsync(location.RootId, location.AnimeDirectory, cancellationToken);
            var summary = $" Library reconciled: {result.Discovered} added, {result.Updated} changed, {result.Removed} removed.";
            if (operationId is { } id)
            {
                await new OperationStore(db).AppendLogAsync(id, OperationLogLevel.Information, LogModule, summary.Trim(), cancellationToken);
            }

            return summary;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Library reconciliation after an anime import failed for {Directory}.", location.AnimeDirectory);
            if (operationId is { } id)
            {
                await new OperationStore(db).AppendLogAsync(id, OperationLogLevel.Warning, LogModule, $"Library reconciliation failed; rescan the root manually. {exception.Message}", cancellationToken);
            }

            return " Library reconciliation failed; rescan the root manually.";
        }
    }

    private async Task<AnimeImportRecord> FinishAsync(
        AnimeImportRecord record,
        AnimeImportStatus status,
        string message,
        Guid? operationId,
        CancellationToken cancellationToken,
        bool finalizeOperation = true)
    {
        var finished = record with
        {
            Status = status,
            Message = message,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await imports.UpsertAsync(finished, cancellationToken);

        if (record.AcquisitionId is { } acquisitionId)
        {
            var jobStatus = status switch
            {
                AnimeImportStatus.Imported => AcquisitionOwnershipStatus.Completed,
                AnimeImportStatus.Failed => AcquisitionOwnershipStatus.Failed,
                AnimeImportStatus.Dismissed => AcquisitionOwnershipStatus.Cancelled,
                _ => AcquisitionOwnershipStatus.Importing
            };
            var jobId = acquisitionId.ToString();
            await ownershipStore.UpdateAsync(
                state => state.Jobs.TryGetValue(jobId, out var job) && job.Status != jobStatus
                    ? SonarrParallelSafety.RegisterJob(state, job with { Status = jobStatus, UpdatedAtUtc = DateTimeOffset.UtcNow })
                    : state,
                cancellationToken);
        }

        if (operationId is { } id && finalizeOperation)
        {
            var operations = new OperationStore(db);
            if (status == AnimeImportStatus.Failed)
            {
                await operations.MarkFailedAsync(id, message, CancellationToken.None);
            }
            else
            {
                await operations.MarkSucceededAsync(id, message, CancellationToken.None);
            }
        }
        else if (operationId is { } pending)
        {
            await new OperationStore(db).ReportProgressAsync(pending, null, message, cancellationToken: cancellationToken);
        }

        return finished;
    }

    private async Task ReleasePathAsync(string path, string jobId, CancellationToken cancellationToken)
    {
        var normalized = SonarrParallelSafety.NormalizePath(path);
        await ownershipStore.UpdateAsync(
            state =>
            {
                if (!state.Paths.TryGetValue(normalized, out var owned) || owned.JobId != jobId)
                {
                    return state;
                }

                var paths = new Dictionary<string, ManagedMediaPath>(state.Paths, StringComparer.OrdinalIgnoreCase);
                paths.Remove(normalized);
                return state with { Paths = paths };
            },
            cancellationToken);
    }

    private static IReadOnlyList<CompletedDownloadFile> EnumerateDownload(string downloadPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(downloadPath))
            {
                return [new CompletedDownloadFile(Path.GetFullPath(downloadPath), new FileInfo(downloadPath).Length)];
            }

            if (!Directory.Exists(downloadPath))
            {
                error = $"The completed download path does not exist or is not mounted in AniLingo: {downloadPath}";
                return [];
            }

            return Directory
                .EnumerateFiles(downloadPath, "*", SearchOption.AllDirectories)
                .Select(path => new CompletedDownloadFile(Path.GetFullPath(path), new FileInfo(path).Length))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"The completed download path could not be read: {exception.Message}";
            return [];
        }
    }

    private static bool IsSidecarOrJunk(string path) =>
        Path.GetExtension(path) is { } extension &&
        (extension.Equals(".nfo", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".nzb", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".par2", StringComparison.OrdinalIgnoreCase) ||
         extension.Equals(".sfv", StringComparison.OrdinalIgnoreCase));

    private static AnimeImportFileRecord ToRecord(
        PlannedAnimeImport planned,
        AnimeImportFileStatus status,
        string? importedPath,
        string? error) =>
        new(
            planned.Source.Path,
            planned.Source.SizeBytes,
            status,
            planned.Targets.ToArray(),
            planned.SidecarPaths.ToArray(),
            planned.ExistingPathsToReplaceAfterCommit.ToArray(),
            planned.Confidence,
            planned.Reasons.ToArray(),
            importedPath,
            error);
}
