using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Tracking;

public sealed record AniListSyncRunSummary(
    int Evaluated,
    int Written,
    int Failed,
    DateTimeOffset? RateLimitedUntil);

/// <summary>
/// One reconciliation pass of automatic AniList sync. For every profile with
/// automatic sync enabled it finds canonical local progress changed since the
/// profile's last successful sync of that work and hands it to the same
/// <see cref="AniListAccountService"/> state/sync methods used by the manual
/// Sync buttons. It never computes AniList values itself, so every write is
/// forward-only, progress-field-only, blocked for ambiguous or pending-review
/// mappings and verified against protected list fields by that canonical path.
/// </summary>
public sealed class AniListSyncReconciler(
    AppDbContext db,
    AniListAccountStore accounts,
    AniListSyncStateStore states,
    AniListRateLimitGate rateLimit,
    Func<string, AniListAccountService> serviceForProfile,
    TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>Works evaluated per pass across all profiles; keeps well below AniList's rate limit.</summary>
    public const int BatchSize = 5;

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan ContinuousDebounce = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(6);
    public static readonly TimeSpan BlockedRecheck = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxBlockedRecheck = TimeSpan.FromHours(24);

    public async Task<AniListSyncRunSummary> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (rateLimit.BlockedUntil(now) is DateTimeOffset pausedUntil)
        {
            return new AniListSyncRunSummary(0, 0, 0, pausedUntil);
        }

        var profileIds = await db.OwnerAccounts
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var profiles = new Dictionary<string, AniListSyncState>(StringComparer.Ordinal);
        var due = new List<(string ProfileId, AniListSyncCheckpoint Checkpoint)>();

        foreach (var profileId in profileIds)
        {
            var account = await accounts.LoadAsync(profileId, cancellationToken);
            if (account is null ||
                account.SyncMode == AniListSyncMode.Off ||
                account.SyncEnabledAt is null ||
                account.TokenExpiresAt <= now)
            {
                continue;
            }

            var state = await states.LoadAsync(
                profileId,
                account.ViewerId,
                cancellationToken);
            profiles[profileId] = state;

            var checkpoints = await AniListSyncCheckpoints.LoadChangedAsync(
                db,
                profileId,
                account.SyncEnabledAt.Value.UtcDateTime,
                cancellationToken);

            due.AddRange(checkpoints
                .Where(checkpoint => IsDue(
                    account.SyncMode,
                    checkpoint,
                    state.Find(checkpoint.MediaKind, checkpoint.LocalId),
                    now))
                .Select(checkpoint => (profileId, checkpoint)));
        }

        var evaluated = 0;
        var written = 0;
        var failed = 0;
        DateTimeOffset? rateLimitedUntil = null;
        var changedProfiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profileGroup in due
                     .OrderBy(x => x.Checkpoint.UpdatedAt)
                     .Take(BatchSize)
                     .GroupBy(x => x.ProfileId))
        {
            var service = serviceForProfile(profileGroup.Key);
            foreach (var (profileId, checkpoint) in profileGroup)
            {
                var state = profiles[profileId];
                var previous = state.Find(checkpoint.MediaKind, checkpoint.LocalId);
                var outcome = await EvaluateAsync(service, checkpoint, cancellationToken);
                var attemptedAt = timeProvider.GetUtcNow();
                evaluated++;
                changedProfiles.Add(profileId);

                if (outcome is null)
                {
                    // The local work no longer exists.
                    profiles[profileId] = Without(state, checkpoint);
                    continue;
                }

                rateLimitedUntil = rateLimit.BlockedUntil(attemptedAt);
                if (rateLimitedUntil is not null && outcome.Status == AniListSyncItemStatus.Failed)
                {
                    outcome = outcome with
                    {
                        Message = $"AniList rate limit reached. Automatic sync resumes after {rateLimitedUntil.Value:u}."
                    };
                }

                var item = Apply(previous, checkpoint, outcome, attemptedAt, rateLimitedUntil);
                profiles[profileId] = With(state, item);

                if (item.Status == AniListSyncItemStatus.Synced)
                {
                    written++;
                }
                else if (item.Status == AniListSyncItemStatus.Failed)
                {
                    failed++;
                    logger.LogWarning(
                        "Automatic AniList sync failed for {MediaKind} {LocalId} of profile {ProfileId}: {Message}",
                        checkpoint.MediaKind,
                        checkpoint.LocalId,
                        profileId,
                        item.Message);
                }

                if (rateLimitedUntil is not null)
                {
                    break;
                }
            }

            if (rateLimitedUntil is not null)
            {
                break;
            }
        }

        foreach (var profileId in changedProfiles)
        {
            await states.SaveAsync(profileId, profiles[profileId], cancellationToken);
        }

        return new AniListSyncRunSummary(evaluated, written, failed, rateLimitedUntil);
    }

    /// <summary>
    /// On completion: due when a new unit (episode, chapter) was finished since
    /// the last successful sync. Continuous: due for any newer checkpoint once
    /// watching/reading of that work has paused for <see cref="ContinuousDebounce"/>.
    /// Items waiting for a retry or recheck are never due early.
    /// </summary>
    public static bool IsDue(
        AniListSyncMode mode,
        AniListSyncCheckpoint checkpoint,
        AniListSyncItem? item,
        DateTimeOffset now)
    {
        if (item?.NextAttemptAt > now ||
            item?.HandledLocalUpdatedAt >= checkpoint.UpdatedAt)
        {
            return false;
        }

        return mode switch
        {
            AniListSyncMode.OnCompletion =>
                checkpoint.CompletionMarker is not null &&
                !string.Equals(
                    checkpoint.CompletionMarker,
                    item?.HandledCompletionMarker,
                    StringComparison.Ordinal),
            AniListSyncMode.Continuous =>
                now.UtcDateTime - checkpoint.UpdatedAt >= ContinuousDebounce,
            _ => false
        };
    }

    /// <summary>
    /// Exponential delay after <paramref name="attempts"/> consecutive
    /// unsuccessful attempts: AniList failures start at one minute (max 6 h),
    /// safety blocks are rechecked from one hour (max 24 h).
    /// </summary>
    public static TimeSpan RetryDelay(AniListSyncItemStatus status, int attempts)
    {
        var (initial, max) = status == AniListSyncItemStatus.Blocked
            ? (BlockedRecheck, MaxBlockedRecheck)
            : (BaseBackoff, MaxBackoff);
        var exponent = Math.Clamp(attempts - 1, 0, 16);
        var delay = TimeSpan.FromTicks(initial.Ticks * (1L << exponent));
        return delay > max ? max : delay;
    }

    /// <summary>Profile identity for an <see cref="AniListAccountService"/> used outside a request.</summary>
    public static CurrentAccountContext ProfileAccount(string profileId) =>
        new(new ProfileHttpContextAccessor(profileId));

    private async Task<Outcome?> EvaluateAsync(
        AniListAccountService service,
        AniListSyncCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = checkpoint.MediaKind switch
            {
                AniListSyncCheckpoints.Anime => await service.GetAnimeProgressSummaryAsync(checkpoint.LocalId, cancellationToken),
                AniListSyncCheckpoints.Manga => await service.GetMangaProgressSummaryAsync(checkpoint.LocalId, cancellationToken),
                _ => await service.GetNovelProgressSummaryAsync(checkpoint.LocalId, cancellationToken)
            };

            if (summary is null)
            {
                return null;
            }

            switch (summary.MappingBasis)
            {
                case ExternalMappingBasis.NotMatched:
                    return new Outcome(
                        AniListSyncItemStatus.NotMatched,
                        "Not matched to AniList; progress stays local.",
                        null,
                        null);
                case ExternalMappingBasis.NeedsReview:
                    return new Outcome(
                        AniListSyncItemStatus.Blocked,
                        summary.ReviewReason ?? "The AniList mapping needs review before progress can sync.",
                        null,
                        null);
            }

            // Canonical state first: it refuses pending mapping reviews before
            // contacting AniList and classifies the remote entry.
            var state = checkpoint.MediaKind switch
            {
                AniListSyncCheckpoints.Anime => await service.GetAnimeProgressStateAsync(checkpoint.LocalId, cancellationToken),
                AniListSyncCheckpoints.Manga => await service.GetMangaProgressStateAsync(checkpoint.LocalId, cancellationToken),
                _ => await service.GetNovelProgressStateAsync(checkpoint.LocalId, cancellationToken)
            };

            switch (state.Kind)
            {
                case AniListExternalProgressStateKind.Synced or
                    AniListExternalProgressStateKind.AniListAhead or
                    AniListExternalProgressStateKind.NoLocalProgress:
                    return new Outcome(
                        AniListSyncItemStatus.UpToDate,
                        state.Message,
                        state.LocalProgress,
                        state.RemoteProgress);
                case AniListExternalProgressStateKind.RemoteUnavailable:
                    return new Outcome(
                        AniListSyncItemStatus.Failed,
                        state.Message,
                        state.LocalProgress,
                        state.RemoteProgress);
                case AniListExternalProgressStateKind.LocalAhead when state.CanSync:
                    break;
                default:
                    return new Outcome(
                        AniListSyncItemStatus.Blocked,
                        state.Message,
                        state.LocalProgress,
                        state.RemoteProgress);
            }

            // The sync call re-reads the remote entry immediately before writing.
            var result = checkpoint.MediaKind switch
            {
                AniListSyncCheckpoints.Anime => await service.SyncAnimeProgressAsync(checkpoint.LocalId, cancellationToken),
                AniListSyncCheckpoints.Manga => await service.SyncMangaProgressAsync(checkpoint.LocalId, cancellationToken),
                _ => await service.SyncNovelProgressAsync(checkpoint.LocalId, cancellationToken)
            };

            return new Outcome(
                result.Success
                    ? result.Changed ? AniListSyncItemStatus.Synced : AniListSyncItemStatus.UpToDate
                    : AniListSyncItemStatus.Failed,
                result.Message,
                state.LocalProgress,
                result.Changed ? state.LocalProgress : state.RemoteProgress);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Isolate one broken work so it cannot starve the rest of the queue;
            // it is retried with backoff and the details stay in the server log.
            logger.LogError(
                exception,
                "Automatic AniList sync failed unexpectedly for {MediaKind} {LocalId}.",
                checkpoint.MediaKind,
                checkpoint.LocalId);
            return new Outcome(
                AniListSyncItemStatus.Failed,
                "Automatic sync failed unexpectedly. Details are in the server log.",
                null,
                null);
        }
    }

    private static AniListSyncItem Apply(
        AniListSyncItem? previous,
        AniListSyncCheckpoint checkpoint,
        Outcome outcome,
        DateTimeOffset now,
        DateTimeOffset? rateLimitedUntil)
    {
        var succeeded = outcome.Status is
            AniListSyncItemStatus.Synced or
            AniListSyncItemStatus.UpToDate or
            AniListSyncItemStatus.NotMatched;
        var failureCount = succeeded
            ? 0
            : (previous?.FailureCount ?? 0) + 1;

        DateTimeOffset? nextAttemptAt = succeeded
            ? null
            : Later(now + RetryDelay(outcome.Status, failureCount), rateLimitedUntil);

        return new AniListSyncItem(
            checkpoint.MediaKind,
            checkpoint.LocalId,
            checkpoint.Title,
            outcome.Status,
            outcome.Message,
            succeeded ? checkpoint.UpdatedAt : previous?.HandledLocalUpdatedAt,
            succeeded ? checkpoint.CompletionMarker : previous?.HandledCompletionMarker,
            outcome.LocalProgress ?? previous?.LocalProgress,
            outcome.RemoteProgress ?? previous?.RemoteProgress,
            now,
            succeeded ? now : previous?.LastSuccessAt,
            failureCount,
            nextAttemptAt);
    }

    private static DateTimeOffset Later(DateTimeOffset value, DateTimeOffset? other) =>
        other > value ? other.Value : value;

    private static AniListSyncState With(AniListSyncState state, AniListSyncItem item) =>
        state with
        {
            Items = [item, .. Without(state, item.MediaKind, item.LocalId).Items]
        };

    private static AniListSyncState Without(AniListSyncState state, AniListSyncCheckpoint checkpoint) =>
        Without(state, checkpoint.MediaKind, checkpoint.LocalId);

    private static AniListSyncState Without(AniListSyncState state, string mediaKind, Guid localId) =>
        state with
        {
            Items = state.Items
                .Where(x => x.LocalId != localId ||
                    !string.Equals(x.MediaKind, mediaKind, StringComparison.Ordinal))
                .ToArray()
        };

    private sealed record Outcome(
        AniListSyncItemStatus Status,
        string Message,
        int? LocalProgress,
        int? RemoteProgress);

    /// <summary>
    /// Supplies the profile identity to <see cref="CurrentAccountContext"/> for
    /// background work. It is instance-bound (not the shared AsyncLocal of
    /// <see cref="HttpContextAccessor"/>), so concurrent profiles never mix.
    /// </summary>
    private sealed class ProfileHttpContextAccessor(string profileId) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, AniListAccountStore.ValidateProfileId(profileId))],
                    "anilist-sync"))
        };
    }
}
