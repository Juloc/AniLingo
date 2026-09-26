using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;

namespace AniLingo.Web.Features.Tracking;

public sealed record AniListSyncOverview(
    AniListSyncMode Mode,
    DateTimeOffset? EnabledAt,
    IReadOnlyList<AniListSyncItem> Activity,
    DateTimeOffset? PausedUntil);

/// <summary>
/// Current profile's automatic sync setting and recent activity for the
/// AniList settings page. Everything is read and written for the signed-in
/// profile only.
/// </summary>
public sealed class AniListSyncService(
    AniListAccountStore accounts,
    AniListSyncStateStore states,
    AniListRateLimitGate rateLimit,
    CurrentAccountContext currentAccount,
    TimeProvider timeProvider)
{
    public const int ActivityLimit = 20;

    public async Task<AniListSyncOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var account = await accounts.LoadAsync(currentAccount.ProfileId, cancellationToken);
        if (account is null)
        {
            return new AniListSyncOverview(AniListSyncMode.Off, null, [], null);
        }

        var state = await states.LoadAsync(
            currentAccount.ProfileId,
            account.ViewerId,
            cancellationToken);

        return new AniListSyncOverview(
            account.SyncMode,
            account.SyncEnabledAt,
            state.Items
                .Where(x => x.Status != AniListSyncItemStatus.NotMatched)
                .OrderByDescending(x => x.LastAttemptAt)
                .Take(ActivityLimit)
                .ToArray(),
            account.SyncMode == AniListSyncMode.Off
                ? null
                : rateLimit.BlockedUntil(timeProvider.GetUtcNow()));
    }

    public Task<bool> SetModeAsync(AniListSyncMode mode, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return accounts.UpdateSyncModeAsync(
            currentAccount.ProfileId,
            mode,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

/// <summary>
/// In-process loop that runs <see cref="AniListSyncReconciler"/> once per
/// <see cref="AniListSyncReconciler.Interval"/>. Each pass gets its own DI
/// scope; each profile gets its own <see cref="AniListAccountService"/> bound
/// to that profile's identity and AniList token.
/// </summary>
public sealed class AniListSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    AniListAccountStore accounts,
    AniListSyncStateStore states,
    AniListRateLimitGate rateLimit,
    TimeProvider timeProvider,
    ILogger<AniListSyncBackgroundService> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
            using var timer = new PeriodicTimer(AniListSyncReconciler.Interval, timeProvider);
            do
            {
                await RunPassAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var httpClients = services.GetRequiredService<IHttpClientFactory>();
            var reconciler = new AniListSyncReconciler(
                services.GetRequiredService<AppDbContext>(),
                accounts,
                states,
                rateLimit,
                profileId => ActivatorUtilities.CreateInstance<AniListAccountService>(
                    services,
                    httpClients.CreateClient(nameof(AniListAccountService)),
                    AniListSyncReconciler.ProfileAccount(profileId)),
                timeProvider,
                logger);

            var summary = await reconciler.RunOnceAsync(cancellationToken);
            if (summary.Evaluated > 0)
            {
                logger.LogInformation(
                    "Automatic AniList sync evaluated {Evaluated} work(s): {Written} written, {Failed} failed.",
                    summary.Evaluated,
                    summary.Written,
                    summary.Failed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Keep the loop alive; the next pass retries from the persisted cursors.
            logger.LogError(exception, "Automatic AniList sync pass failed.");
        }
    }
}
