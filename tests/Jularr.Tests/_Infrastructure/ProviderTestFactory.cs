using Jularr.Web.Features.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>Builds provider-framework (#438) components for tests.</summary>
internal static class ProviderTestFactory
{
    public static ProviderExecutor NewExecutor(
        TimeProvider? clock = null,
        ProviderRateLimiter? rateLimiter = null,
        ProviderHealthTracker? health = null)
    {
        clock ??= TimeProvider.System;
        return new ProviderExecutor(
            rateLimiter ?? new ProviderRateLimiter(),
            health ?? new ProviderHealthTracker(clock),
            clock,
            NullLogger<ProviderExecutor>.Instance);
    }
}
