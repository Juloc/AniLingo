namespace Jularr.Web.Features.Franchises;

public sealed class FranchiseRefreshService(
    IServiceScopeFactory scopes,
    ILogger<FranchiseRefreshService> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<FranchiseStore>();
                    var service = scope.ServiceProvider.GetRequiredService<FranchiseService>();
                    foreach (var franchiseId in await store.ListFollowedFranchiseIdsAsync(stoppingToken))
                    {
                        try
                        {
                            await service.RefreshAsync(franchiseId, stoppingToken);
                        }
                        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                        {
                            logger.LogInformation(
                                exception,
                                "Franchise {FranchiseId} could not be refreshed; local follow state is unchanged.",
                                franchiseId);
                        }
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Refreshing followed franchises failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
