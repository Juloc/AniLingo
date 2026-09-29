using Jularr.Web.Features.Auth;

namespace Jularr.Web.Features.OfflineLibrary;

/// <summary>
/// Server-side entry point of smart offline prefetch (#415): loads the current
/// profile's policy, selects the next-up candidates from canonical progress and
/// lets <see cref="OfflinePrefetchPlanner"/> decide against the device's own
/// inventory. The client then downloads and deletes through the existing
/// offline endpoints; nothing here writes content or progress.
/// </summary>
public sealed class OfflinePrefetchService(
    OfflinePrefetchPolicyStore store,
    OfflinePrefetchCandidateSource candidates,
    CurrentAccountContext currentAccount)
{
    public Task<OfflinePrefetchPolicy> GetPolicyAsync(CancellationToken cancellationToken = default) =>
        store.LoadAsync(currentAccount.ProfileId, cancellationToken);

    public async Task<(OfflinePrefetchPolicy Policy, OfflinePrefetchPlan Plan)> PlanAsync(
        OfflinePrefetchDeviceState device,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        if (!policy.Enabled)
        {
            return (policy, OfflinePrefetchPlanner.Plan(policy, [], device));
        }

        var next = await candidates.GetAsync(policy, cancellationToken);
        return (policy, OfflinePrefetchPlanner.Plan(policy, next, device));
    }
}

public static class OfflinePrefetchRegistration
{
    public static IServiceCollection AddOfflinePrefetch(
        this IServiceCollection services,
        string dataRoot = "/data")
    {
        services.AddSingleton(provider => new OfflinePrefetchPolicyStore(
            dataRoot,
            provider.GetService<ILogger<OfflinePrefetchPolicyStore>>()));
        services.AddScoped<OfflinePrefetchCandidateSource>();
        services.AddScoped<OfflinePrefetchService>();
        return services;
    }
}
