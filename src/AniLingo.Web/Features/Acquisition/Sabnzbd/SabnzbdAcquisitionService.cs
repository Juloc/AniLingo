namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public sealed class SabnzbdAcquisitionService(
    ISabnzbdClient client,
    SabnzbdAcquisitionStore store)
{
    public async Task<SabnzbdAcquisitionJob> GrabAsync(
        SabnzbdConnection connection,
        Guid animeId,
        IReadOnlyCollection<Guid> episodeIds,
        string releaseIdentity,
        string releaseTitle,
        Uri nzbUrl,
        CancellationToken cancellationToken)
    {
        if (animeId == Guid.Empty)
        {
            throw new ArgumentException("Anime ID is required.", nameof(animeId));
        }

        if (episodeIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Episode IDs must not contain empty values.",
                nameof(episodeIds));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(releaseIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseTitle);

        var result = await client.GrabAsync(
            connection,
            new SabnzbdGrabRequest(
                nzbUrl,
                NzbName: releaseTitle,
                Category: connection.Settings.Category),
            cancellationToken);

        if (!result.Success || result.NzoIds.Count == 0)
        {
            throw new SabnzbdException(
                result.Error ?? "SABnzbd did not accept the release.");
        }

        if (result.NzoIds.Count != 1)
        {
            throw new SabnzbdException(
                "SABnzbd returned multiple job IDs for one acquisition request.");
        }

        var now = DateTimeOffset.UtcNow;
        var job = new SabnzbdAcquisitionJob(
            Guid.NewGuid(),
            result.NzoIds[0],
            animeId,
            episodeIds.Distinct().ToArray(),
            releaseIdentity.Trim(),
            releaseTitle.Trim(),
            SabnzbdAcquisitionState.Queued,
            Attempt: 1,
            SabnzbdFailureKind.None,
            FailureMessage: null,
            CreatedAt: now,
            UpdatedAt: now);

        return await store.AddAsync(job, cancellationToken);
    }

    public async Task<IReadOnlyList<SabnzbdAcquisitionJob>> ReconcileAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken)
    {
        var tracked = await store.LoadAsync(cancellationToken);
        if (tracked.Count == 0)
        {
            return tracked;
        }

        var queue = await client.GetQueueAsync(connection, cancellationToken);
        var queueById = queue.Jobs.ToDictionary(
            job => job.NzoId,
            StringComparer.OrdinalIgnoreCase);

        var activeIds = tracked
            .Where(job =>
                job.State is SabnzbdAcquisitionState.Queued
                    or SabnzbdAcquisitionState.Downloading
                    or SabnzbdAcquisitionState.Processing)
            .Select(job => job.NzoId)
            .ToArray();

        var history = activeIds.Length == 0
            ? new SabnzbdHistorySnapshot([])
            : await client.GetHistoryAsync(
                connection,
                activeIds,
                cancellationToken);

        var historyById = history.Jobs.ToDictionary(
            job => job.NzoId,
            StringComparer.OrdinalIgnoreCase);

        foreach (var job in tracked)
        {
            if (queueById.TryGetValue(job.NzoId, out var queueJob))
            {
                var state = MapQueueState(queueJob.Status);
                await store.UpdateStateAsync(
                    job.NzoId,
                    state,
                    cancellationToken: cancellationToken);
                continue;
            }

            if (!historyById.TryGetValue(job.NzoId, out var historyJob))
            {
                continue;
            }

            var completed = historyJob.FailureKind == SabnzbdFailureKind.None &&
                            IsCompleted(historyJob.Status);

            await store.UpdateStateAsync(
                job.NzoId,
                completed
                    ? SabnzbdAcquisitionState.Completed
                    : SabnzbdAcquisitionState.Failed,
                historyJob.FailureKind,
                historyJob.FailureMessage,
                cancellationToken: cancellationToken);
        }

        return await store.LoadAsync(cancellationToken);
    }

    public async Task<SabnzbdAcquisitionJob?> RetryAsync(
        SabnzbdConnection connection,
        string nzoId,
        CancellationToken cancellationToken)
    {
        var result = await client.RetryAsync(
            connection,
            nzoId,
            cancellationToken);

        if (!result.Success || string.IsNullOrWhiteSpace(result.NewNzoId))
        {
            throw new SabnzbdException(
                result.Error ?? "SABnzbd retry did not return a new job ID.");
        }

        return await store.UpdateStateAsync(
            nzoId,
            SabnzbdAcquisitionState.Queued,
            SabnzbdFailureKind.None,
            failureMessage: null,
            replacementNzoId: result.NewNzoId,
            cancellationToken: cancellationToken);
    }

    private static SabnzbdAcquisitionState MapQueueState(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return SabnzbdAcquisitionState.Downloading;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized.Contains("queue", StringComparison.Ordinal) ||
               normalized.Contains("pause", StringComparison.Ordinal)
            ? SabnzbdAcquisitionState.Queued
            : normalized.Contains("unpack", StringComparison.Ordinal) ||
              normalized.Contains("verify", StringComparison.Ordinal) ||
              normalized.Contains("repair", StringComparison.Ordinal)
                ? SabnzbdAcquisitionState.Processing
                : SabnzbdAcquisitionState.Downloading;
    }

    private static bool IsCompleted(string? status) =>
        status?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true;
}
