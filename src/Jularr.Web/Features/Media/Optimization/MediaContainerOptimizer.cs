using System.Data.Common;
using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Media.Compatibility;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Media.Optimization;

public enum MediaOptimizationFileStatus
{
    Optimized,
    // Already plays directly wherever a remux could make it play.
    Unchanged,
    // Not optimized because something would be lost, changed or could not be verified.
    Kept,
    // Nothing to do (no longer in the library, source changed during the run).
    Skipped,
    // ffprobe/ffmpeg failed; the source is untouched.
    Failed
}

public sealed record MediaOptimizationFileResult(
    Guid MediaFileId,
    string FileName,
    MediaOptimizationFileStatus Status,
    string Message);

public sealed class MediaOptimizationOptions
{
    public TimeSpan BusyPollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

// Post-download lossless container optimization: analyze -> decide -> stream-copy remux into a
// partial file -> verify it with ffprobe -> adopt it atomically. The source is only removed after
// the output was verified, renamed into place and recorded as the library file; every step before
// that leaves the source as the canonical file, and the journal lets a restart finish or undo an
// interrupted commit. Never re-encodes: the only ffmpeg call is IMediaContainerRemuxer (-c copy).
public sealed class MediaContainerOptimizer(
    AppDbContext db,
    IMediaProbeRunner probeRunner,
    IMediaContainerRemuxer remuxer,
    MediaInventoryService inventory,
    MediaOptimizationJournal journal,
    IEnumerable<IMediaFileReplacementParticipant> participants,
    ILogger<MediaContainerOptimizer> logger,
    MediaOptimizationOptions? options = null)
{
    public const string OperationKind = "media-optimization";

    // The startup run that finishes or undoes a file swap a crash interrupted; the admin activity files
    // it under Repack, while the run that rewrites the container (OperationKind) is the Remux.
    public const string RecoveryOperationKind = "media-optimization-recovery";
    public const string LogModule = "Optimization";
    public const string PartialSuffix = ".jularr-partial";

    private readonly MediaOptimizationOptions timing = options ?? new MediaOptimizationOptions();

    public async Task<IReadOnlyList<MediaOptimizationFileResult>> OptimizeAsync(
        IReadOnlyList<Guid> mediaFileIds,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaFileIds);

        var results = new List<MediaOptimizationFileResult>();
        foreach (var mediaFileId in mediaFileIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await OptimizeFileAsync(mediaFileId, operation, cancellationToken);
            results.Add(result);
            await LogAsync(
                operation,
                result.Status == MediaOptimizationFileStatus.Failed ? OperationLogLevel.Warning : OperationLogLevel.Information,
                $"{result.FileName}: {result.Message}");
        }

        if (operation is not null && results.Count > 0)
        {
            await operation.ReportAsync(100, Summarize(results), cancellationToken: CancellationToken.None);
        }

        // A tool failure (not a deliberate "keep") marks the Operation failed so it can be retried.
        if (results.Any(x => x.Status == MediaOptimizationFileStatus.Failed))
        {
            throw new InvalidOperationException(Summarize(results));
        }

        return results;
    }

    public static string Summarize(IReadOnlyList<MediaOptimizationFileResult> results)
    {
        if (results.Count == 1)
        {
            return results[0].Message;
        }

        var optimized = results.Count(x => x.Status == MediaOptimizationFileStatus.Optimized);
        var failed = results.Count(x => x.Status == MediaOptimizationFileStatus.Failed);
        return $"{optimized} of {results.Count} file(s) optimized for Direct Play; {results.Count - optimized - failed} left unchanged, {failed} failed.";
    }

    public async Task<MediaOptimizationFileResult> OptimizeFileAsync(
        Guid mediaFileId,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        if (journal.Read(mediaFileId) is { } pending)
        {
            await RecoverEntryAsync(pending, operation);
        }

        var media = await LoadMediaAsync(mediaFileId, cancellationToken);
        if (media is null)
        {
            return new(mediaFileId, "", MediaOptimizationFileStatus.Skipped, "The media file is no longer in the library.");
        }

        var sourcePath = Path.GetFullPath(media.Path);
        var name = Path.GetFileName(sourcePath);
        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            return new(mediaFileId, name, MediaOptimizationFileStatus.Skipped, "The media file does not exist on disk.");
        }

        await ReportAsync(operation, 10, $"Analyzing media: {name}", cancellationToken);
        var (sourceDetail, analysisError) = await ProbeDetailAsync(sourcePath, cancellationToken);
        if (sourceDetail is null)
        {
            return new(mediaFileId, name, MediaOptimizationFileStatus.Failed, $"Media analysis failed: {analysisError}");
        }

        var decision = MediaOptimizationPlanner.Decide(sourceDetail, sourcePath);
        if (decision.Outcome != MediaOptimizationOutcome.Remux || decision.Plan is not { } plan)
        {
            return new(
                mediaFileId,
                name,
                decision.Outcome == MediaOptimizationOutcome.AlreadyOptimal
                    ? MediaOptimizationFileStatus.Unchanged
                    : MediaOptimizationFileStatus.Kept,
                decision.Summary);
        }

        var targetPath = Path.ChangeExtension(sourcePath, plan.TargetExtension);
        if (File.Exists(targetPath))
        {
            return new(mediaFileId, name, MediaOptimizationFileStatus.Kept, $"Kept original: {Path.GetFileName(targetPath)} already exists.");
        }

        if (!HasFreeSpaceFor(sourcePath, source.Length))
        {
            return new(mediaFileId, name, MediaOptimizationFileStatus.Kept, "Kept original: not enough free space for the remuxed copy.");
        }

        var entry = new MediaOptimizationJournalEntry(
            mediaFileId,
            sourcePath,
            targetPath,
            targetPath + PartialSuffix,
            source.Length,
            MediaOptimizationStage.Remuxing,
            null,
            DateTime.UtcNow);

        try
        {
            return await RemuxAndAdoptAsync(entry, source, sourceDetail, plan, decision, operation, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // Nothing was adopted: RemuxAndAdoptAsync handles every failure after the rename itself.
            if (journal.Read(mediaFileId) is { Stage: MediaOptimizationStage.Remuxing })
            {
                Discard(entry);
            }

            if (exception is OperationCanceledException)
            {
                throw;
            }

            logger.LogWarning(exception, "Lossless optimization of {MediaPath} failed.", sourcePath);
            return new(mediaFileId, name, MediaOptimizationFileStatus.Failed, $"Optimization failed; original kept. {exception.Message}");
        }
    }

    // Finishes or undoes every optimization a crash or restart interrupted.
    public async Task<int> RecoverAsync(
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        var recovered = 0;
        foreach (var entry in journal.ReadAll())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RecoverEntryAsync(entry, operation);
            recovered++;
        }

        return recovered;
    }

    private async Task<MediaOptimizationFileResult> RemuxAndAdoptAsync(
        MediaOptimizationJournalEntry entry,
        FileInfo source,
        MediaProbeDetail sourceDetail,
        MediaRemuxPlan plan,
        MediaOptimizationDecision decision,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(entry.SourcePath);
        var sourceWriteTime = source.LastWriteTimeUtc;

        journal.Write(entry);
        TryDelete(entry.PartialPath);

        await LogAsync(operation, OperationLogLevel.Information, $"{name}: {decision.Summary}");
        await ReportAsync(operation, 30, $"Optimizing container: {name}", cancellationToken);
        var run = await remuxer.RemuxAsync(entry.SourcePath, entry.PartialPath, plan, cancellationToken);
        if (!run.Succeeded || !File.Exists(entry.PartialPath))
        {
            Discard(entry);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Failed, $"Remux failed; original kept. {run.Error}");
        }

        await ReportAsync(operation, 80, $"Verifying optimized media: {name}", cancellationToken);
        var (outputDetail, outputError) = await ProbeDetailAsync(entry.PartialPath, cancellationToken);
        var outputSize = new FileInfo(entry.PartialPath).Length;
        var problems = outputDetail is null
            ? [$"The remuxed file could not be analyzed: {outputError}"]
            : MediaRemuxVerifier.Verify(sourceDetail, outputDetail, plan.TargetContainer, entry.TargetPath, entry.SourceSizeBytes, outputSize);
        if (problems.Count > 0)
        {
            Discard(entry);
            return new(
                entry.MediaFileId,
                name,
                MediaOptimizationFileStatus.Kept,
                $"Kept original: the remuxed file failed verification. {string.Join(" ", problems)}");
        }

        var replacement = new MediaFileReplacement(entry.MediaFileId, entry.SourcePath, entry.TargetPath);
        var (check, leases) = await WaitUntilReplaceableAsync(replacement, operation, cancellationToken);
        if (check.Verdict != MediaReplacementVerdict.Allowed)
        {
            Discard(entry);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Kept, $"Kept original: {check.Reason}");
        }

        try
        {
            if (await AdoptAsync(entry, replacement, sourceWriteTime, outputSize, operation, cancellationToken) is { } notAdopted)
            {
                return notAdopted;
            }
        }
        finally
        {
            Release(leases);
        }

        await RefreshInventoryAsync(entry);
        var preserved = DescribePreserved(sourceDetail);
        return new(
            entry.MediaFileId,
            name,
            MediaOptimizationFileStatus.Optimized,
            $"Remuxed to {Path.GetFileName(entry.TargetPath)} without re-encoding ({preserved}); plays directly in {string.Join(", ", decision.TargetDirectPlay.Select(x => x.Name))}.");
    }

    // Runs while every participant's lease is held. Returns null once the database names the
    // output, or the result explaining why the source stayed the library file.
    private async Task<MediaOptimizationFileResult?> AdoptAsync(
        MediaOptimizationJournalEntry entry,
        MediaFileReplacement replacement,
        DateTime sourceWriteTime,
        long outputSize,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(entry.SourcePath);

        // The file may have been upgraded, renamed or removed while ffmpeg ran.
        var current = await LoadMediaAsync(entry.MediaFileId, cancellationToken);
        var sourceNow = new FileInfo(entry.SourcePath);
        if (current is null ||
            !string.Equals(Path.GetFullPath(current.Path), entry.SourcePath, StringComparison.Ordinal) ||
            !sourceNow.Exists ||
            sourceNow.Length != entry.SourceSizeBytes ||
            sourceNow.LastWriteTimeUtc != sourceWriteTime)
        {
            Discard(entry);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Skipped, "The source changed during optimization; it was left as is.");
        }

        if (File.Exists(entry.TargetPath))
        {
            Discard(entry);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Kept, $"Kept original: {Path.GetFileName(entry.TargetPath)} appeared during optimization.");
        }

        CopyUnixFileMode(entry.SourcePath, entry.PartialPath);
        var committing = entry with { Stage = MediaOptimizationStage.Committing, OutputSizeBytes = outputSize };
        journal.Write(committing);
        try
        {
            File.Move(entry.PartialPath, entry.TargetPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(entry);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Failed, $"The optimized file could not be moved into place; original kept. {exception.Message}");
        }

        try
        {
            await AdoptRecordsAsync(replacement);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or IOException)
        {
            // The database still names the source: undo the rename so the library stays as it was.
            logger.LogError(exception, "Could not record the optimized file {TargetPath}; rolling back.", entry.TargetPath);
            TryDelete(entry.TargetPath);
            journal.Delete(entry.MediaFileId);
            return new(entry.MediaFileId, name, MediaOptimizationFileStatus.Failed, "The optimized file could not be recorded; original kept.");
        }

        await FinishAdoptionAsync(committing, replacement, operation);
        return null;
    }

    // Waits while other library work touches the files (imports and renames finish in seconds),
    // then asks every participant once more right before the replacement.
    private async Task<(MediaReplacementCheck Check, IReadOnlyList<IDisposable> Leases)> WaitUntilReplaceableAsync(
        MediaFileReplacement replacement,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timing.BusyTimeout;
        var reported = false;
        while (true)
        {
            var (check, leases) = await CheckReplaceableAsync(replacement, operation, cancellationToken);
            if (check.Verdict != MediaReplacementVerdict.Wait)
            {
                return (check, leases);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return (new(MediaReplacementVerdict.Blocked, $"the library stayed busy ({check.Reason})."), []);
            }

            if (!reported)
            {
                await ReportAsync(operation, 90, check.Reason ?? "Waiting for other library work.", cancellationToken);
                reported = true;
            }

            await Task.Delay(timing.BusyPollInterval, cancellationToken);
        }
    }

    private async Task<(MediaReplacementCheck Check, IReadOnlyList<IDisposable> Leases)> CheckReplaceableAsync(
        MediaFileReplacement replacement,
        OperationExecutionContext? operation,
        CancellationToken cancellationToken)
    {
        var scan = (await new OperationStore(db).ListAsync(
                new OperationListFilter(View: "active", Kind: LibraryScanCoordinator.OperationKind),
                cancellationToken))
            .FirstOrDefault(x => x.Status == OperationStatus.Running && x.Id != operation?.OperationId);
        if (scan is not null)
        {
            return (new(MediaReplacementVerdict.Wait, $"Waiting for '{scan.Title}' to finish."), []);
        }

        var leases = new List<IDisposable>();
        try
        {
            foreach (var participant in participants)
            {
                var check = await participant.CheckAsync(replacement, cancellationToken);
                if (check.Lease is { } lease)
                {
                    leases.Add(lease);
                }

                if (check.Verdict != MediaReplacementVerdict.Allowed)
                {
                    Release(leases);
                    return (check, []);
                }
            }
        }
        catch
        {
            Release(leases);
            throw;
        }

        return (MediaReplacementCheck.Allowed, leases);
    }

    private static void Release(IEnumerable<IDisposable> leases)
    {
        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    // The path-keyed library records follow the file in one transaction: the MediaFile row keeps
    // its id (progress, segments and episode identity stay attached) and embedded/transcribed
    // subtitle sources, which name the media path, move with it. Stream indices are unchanged.
    private async Task AdoptRecordsAsync(MediaFileReplacement replacement)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);

        var media = await db.MediaFiles.SingleAsync(x => x.Id == replacement.MediaFileId, CancellationToken.None);
        var target = new FileInfo(replacement.TargetPath);
        media.Path = replacement.TargetPath;
        media.SizeBytes = target.Length;
        media.LastWriteTimeUtc = target.LastWriteTimeUtc;

        var prefixes = new[]
        {
            EmbeddedSubtitleExtractor.SourcePrefix,
            EmbeddedSubtitleExtractor.TranscriptionSourcePrefix
        };
        var sourceKeys = prefixes.Select(prefix => prefix + replacement.SourcePath + "#").ToArray();
        var tracks = await db.SubtitleTracks
            .Where(x => x.EpisodeId == media.EpisodeId)
            .ToListAsync(CancellationToken.None);
        foreach (var track in tracks)
        {
            for (var index = 0; index < prefixes.Length; index++)
            {
                if (track.Path.StartsWith(sourceKeys[index], StringComparison.Ordinal))
                {
                    // Keep the "#stream=N" / "#audio=ja" suffix.
                    track.Path = prefixes[index] + replacement.TargetPath + track.Path[(sourceKeys[index].Length - 1)..];
                    break;
                }
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
    }

    // Runs once the database names the target: participants follow, then the source goes. A
    // failure here leaves the journal in Committing, so recovery repeats these idempotent steps.
    private async Task FinishAdoptionAsync(
        MediaOptimizationJournalEntry entry,
        MediaFileReplacement replacement,
        OperationExecutionContext? operation)
    {
        var complete = true;
        foreach (var participant in participants)
        {
            try
            {
                await participant.OnReplacedAsync(replacement, CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                complete = false;
                logger.LogWarning(exception, "Could not update path records after optimizing {TargetPath}.", entry.TargetPath);
            }
        }

        if (!string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal) && File.Exists(entry.SourcePath))
        {
            try
            {
                File.Delete(entry.SourcePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                complete = false;
                await LogAsync(operation, OperationLogLevel.Warning, $"The original {Path.GetFileName(entry.SourcePath)} could not be removed yet: {exception.Message}");
            }
        }

        TryDelete(entry.PartialPath);
        if (complete)
        {
            journal.Delete(entry.MediaFileId);
        }
    }

    // The persisted inventory describes the adopted file from now on.
    private async Task RefreshInventoryAsync(MediaOptimizationJournalEntry entry)
    {
        try
        {
            await inventory.EnsureAnalyzedAsync(entry.MediaFileId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or DbException or DbUpdateException)
        {
            logger.LogWarning(exception, "Could not refresh the media analysis of {TargetPath}.", entry.TargetPath);
        }
    }

    private async Task RecoverEntryAsync(MediaOptimizationJournalEntry entry, OperationExecutionContext? operation)
    {
        var name = Path.GetFileName(entry.SourcePath);
        if (entry.Stage == MediaOptimizationStage.Remuxing)
        {
            Discard(entry);
            await LogAsync(operation, OperationLogLevel.Information, $"{name}: discarded the partial output of an interrupted optimization; the original is unchanged.");
            return;
        }

        var media = await LoadMediaAsync(entry.MediaFileId, CancellationToken.None);
        var recordedPath = media is null ? null : Path.GetFullPath(media.Path);
        var replacement = new MediaFileReplacement(entry.MediaFileId, entry.SourcePath, entry.TargetPath);

        if (string.Equals(recordedPath, entry.TargetPath, StringComparison.Ordinal) && File.Exists(entry.TargetPath))
        {
            // The database already names the output: finish removing the source.
            await FinishAdoptionAsync(entry, replacement, operation);
            await RefreshInventoryAsync(entry);
            await LogAsync(operation, OperationLogLevel.Information, $"{name}: completed an interrupted optimization.");
            return;
        }

        if (string.Equals(recordedPath, entry.SourcePath, StringComparison.Ordinal) && File.Exists(entry.SourcePath))
        {
            // Not recorded yet: the source is still canonical, so drop the renamed output.
            if (File.Exists(entry.TargetPath) && new FileInfo(entry.TargetPath).Length == entry.OutputSizeBytes)
            {
                TryDelete(entry.TargetPath);
            }

            Discard(entry);
            await LogAsync(operation, OperationLogLevel.Information, $"{name}: rolled back an interrupted optimization; the original is unchanged.");
            return;
        }

        // The library moved on (file removed or renamed): leave every media file as it is.
        Discard(entry);
        await LogAsync(operation, OperationLogLevel.Warning, $"{name}: an interrupted optimization no longer matches the library; files were left untouched.");
    }

    private async Task<(MediaProbeDetail? Detail, string? Error)> ProbeDetailAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var run = await probeRunner.ProbeAsync(path, cancellationToken);
        if (run.Status != MediaProbeRunStatus.Completed)
        {
            return (null, (run.Error ?? "ffprobe failed.").Replace(path, Path.GetFileName(path), StringComparison.Ordinal));
        }

        try
        {
            return (MediaProbeParser.ParseDetail(run.Output), null);
        }
        catch (JsonException)
        {
            return (null, "ffprobe returned invalid JSON.");
        }
    }

    private async Task<MediaFile?> LoadMediaAsync(Guid mediaFileId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return await db.MediaFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaFileId, cancellationToken);
    }

    private void Discard(MediaOptimizationJournalEntry entry)
    {
        TryDelete(entry.PartialPath);
        journal.Delete(entry.MediaFileId);
    }

    private static string DescribePreserved(MediaProbeDetail detail)
    {
        var parts = new List<string>
        {
            $"{detail.OfType(MediaProbeStreamType.Audio).Count()} audio",
            $"{detail.OfType(MediaProbeStreamType.Subtitle).Count()} subtitle stream(s)"
        };
        if (detail.Chapters.Count > 0)
        {
            parts.Add($"{detail.Chapters.Count} chapters");
        }

        return string.Join(", ", parts) + " preserved";
    }

    private static bool HasFreeSpaceFor(string path, long requiredBytes)
    {
        try
        {
            var drive = new DriveInfo(Path.GetDirectoryName(path)!);
            return drive.AvailableFreeSpace > requiredBytes + requiredBytes / 20;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Unknown free space: ffmpeg fails on a full disk and the partial output is discarded.
            return true;
        }
    }

    private static void CopyUnixFileMode(string sourcePath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(targetPath, File.GetUnixFileMode(sourcePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ReportAsync(
        OperationExecutionContext? operation,
        int percent,
        string message,
        CancellationToken cancellationToken)
    {
        if (operation is not null)
        {
            await operation.ReportAsync(percent, message, cancellationToken: cancellationToken);
        }
    }

    private async Task LogAsync(OperationExecutionContext? operation, OperationLogLevel level, string message)
    {
        if (operation is not null)
        {
            await operation.LogAsync(level, LogModule, message, CancellationToken.None);
        }
        else
        {
            logger.Log(level == OperationLogLevel.Warning ? LogLevel.Warning : LogLevel.Information, "{Message}", message);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
