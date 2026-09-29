using Microsoft.Extensions.Logging;

namespace Jularr.Web.Features.Providers;

/// <summary>
/// The shared execution path every external provider call runs through, so no
/// provider re-implements networking concerns: rate-limit gate and pacing, bounded
/// retries with exponential backoff, Retry-After handling on HTTP 429, and health
/// (success/failure, circuit) tracking. <see cref="SendAsync"/> is HTTP aware;
/// <see cref="ExecuteAsync{T}"/> wraps any operation (used to migrate clients that
/// already own their <see cref="HttpClient"/>).
/// </summary>
public sealed class ProviderExecutor(
    ProviderRateLimiter rateLimiter,
    ProviderHealthTracker health,
    TimeProvider clock,
    ILogger<ProviderExecutor> logger)
{
    /// <summary>
    /// Runs <paramref name="operation"/> with the framework's resilience policy, recording
    /// health and honouring the rate-limit gate/circuit. Transient failures (network errors,
    /// timeouts) are retried; other exceptions are recorded and propagated without retry.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string providerKey,
        Func<CancellationToken, Task<T>> operation,
        ProviderExecutionPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(operation);
        policy ??= ProviderExecutionPolicy.Default;

        for (var attempt = 1; ; attempt++)
        {
            await PrepareAsync(providerKey, policy, cancellationToken);

            try
            {
                var result = await operation(cancellationToken);
                RecordSuccess(providerKey, policy);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (IsClientError(exception))
                {
                    // The provider answered; the request/credentials/quota were refused.
                    RecordSuccess(providerKey, policy);
                    throw;
                }

                RecordFailure(providerKey, policy, exception.Message);
                if (IsTransient(exception) && attempt < policy.MaxAttempts)
                {
                    logger.LogDebug(
                        exception,
                        "Provider '{Provider}' call failed (attempt {Attempt}/{Max}); retrying.",
                        providerKey, attempt, policy.MaxAttempts);
                    await BackoffAsync(policy, attempt, cancellationToken);
                    continue;
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Sends an HTTP request built by <paramref name="requestFactory"/> (invoked fresh per
    /// attempt) with the framework's resilience policy. On HTTP 429 the Retry-After is recorded
    /// into the provider's rate-limit gate; 5xx and transient network failures are retried when
    /// the policy allows. The final response is returned (including non-success) for the caller to
    /// interpret; the response body is never read here.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(
        string providerKey,
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        ProviderExecutionPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(requestFactory);
        policy ??= ProviderExecutionPolicy.Default;

        for (var attempt = 1; ; attempt++)
        {
            await PrepareAsync(providerKey, policy, cancellationToken);

            HttpResponseMessage response;
            using (var request = requestFactory())
            {
                try
                {
                    response = await httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsTransient(exception))
                {
                    RecordFailure(providerKey, policy, exception.Message);
                    if (attempt < policy.MaxAttempts)
                    {
                        await BackoffAsync(policy, attempt, cancellationToken);
                        continue;
                    }

                    throw;
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = ProviderRetryAfter.Resolve(
                    response, clock.GetUtcNow(), policy.DefaultRetryAfter, policy.MaxRetryAfter);
                rateLimiter.Block(providerKey, clock.GetUtcNow() + retryAfter);
                RecordFailure(providerKey, policy, $"HTTP 429 (retry after {retryAfter.TotalSeconds:F0}s)");

                if (policy.HonorRateLimitGate && attempt < policy.MaxAttempts)
                {
                    response.Dispose();
                    // The next PrepareAsync observes the gate we just set and fails fast, so a
                    // retry only helps once the pause has elapsed; keep it for that case.
                    await BackoffAsync(policy, attempt, cancellationToken);
                    continue;
                }

                return response;
            }

            if ((int)response.StatusCode >= 500)
            {
                RecordFailure(providerKey, policy, $"HTTP {(int)response.StatusCode}");
                if (policy.RetryServerErrors && attempt < policy.MaxAttempts)
                {
                    response.Dispose();
                    await BackoffAsync(policy, attempt, cancellationToken);
                    continue;
                }

                return response;
            }

            if (response.IsSuccessStatusCode)
            {
                RecordSuccess(providerKey, policy);
            }
            else
            {
                // A non-retryable client error (auth, quota, not found, bad request) is a
                // request/credential condition, not an outage: the provider answered, so it does
                // not count toward Degraded/Unavailable or open the circuit (it clears any outage
                // streak like a success). The caller translates the status into its own error.
                RecordSuccess(providerKey, policy);
            }

            return response;
        }
    }

    private async Task PrepareAsync(
        string providerKey,
        ProviderExecutionPolicy policy,
        CancellationToken cancellationToken)
    {
        if (policy.ShortCircuitWhenUnavailable && policy.TrackHealth && !health.IsAvailable(providerKey))
        {
            throw new ProviderUnavailableException(providerKey);
        }

        if (!policy.HonorRateLimitGate && policy.MinSpacing is null)
        {
            return;
        }

        var pause = await rateLimiter.WaitForTurnAsync(
            providerKey, policy.MinSpacing ?? TimeSpan.Zero, clock, cancellationToken);
        if (policy.HonorRateLimitGate && pause is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            throw new ProviderRateLimitedException(providerKey, retryAfter);
        }
    }

    private void RecordSuccess(string providerKey, ProviderExecutionPolicy policy)
    {
        if (policy.TrackHealth)
        {
            health.RecordSuccess(providerKey);
        }
    }

    private void RecordFailure(string providerKey, ProviderExecutionPolicy policy, string error)
    {
        if (policy.TrackHealth)
        {
            health.RecordFailure(providerKey, error);
        }
    }

    private Task BackoffAsync(ProviderExecutionPolicy policy, int attempt, CancellationToken cancellationToken)
    {
        if (policy.BaseBackoff <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var exponent = Math.Min(attempt - 1, 16);
        var ticks = policy.BaseBackoff.Ticks * (1L << exponent);
        var delay = ticks < 0 || ticks > policy.MaxBackoff.Ticks
            ? policy.MaxBackoff
            : TimeSpan.FromTicks(ticks);
        return Task.Delay(delay, clock, cancellationToken);
    }

    /// <summary>
    /// An <see cref="HttpRequestException"/> carrying a 4xx status other than 429 (which the
    /// Retry-After gate owns): a client error, not unavailability. Neither retried nor counted.
    /// </summary>
    private static bool IsClientError(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status != System.Net.HttpStatusCode.TooManyRequests;

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException
            or TimeoutException
            or TaskCanceledException
            or IOException;
}
