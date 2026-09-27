using System.Diagnostics;

namespace Jularr.Web.Features.Ai;

/// <summary>
/// Runs one AI request as a tracked activity: publishes live state, makes the request cancellable,
/// and records exactly one usage measurement — exact tokens when the provider reported them,
/// clearly marked estimates otherwise. Any feature calling a provider can wrap its call here.
/// </summary>
public sealed class AiActivityRunner(
    AiActivityTracker tracker,
    AiUsageTracker usage,
    TimeProvider time)
{
    public async Task<T> RunAsync<T>(
        AiActivityStart start,
        int inputCharacters,
        Func<CancellationToken, Task<T>> call,
        Func<T, int> outputCharacters,
        CancellationToken cancellationToken)
    {
        start = AiWorkScope.Apply(start);
        using var handle = tracker.Start(start, cancellationToken);
        using var scope = AiActivityScope.Enter(handle);
        if (AiWorkScope.Current is { Part: { } part, Parts: { } parts })
        {
            handle.ReportProgress(part, parts);
        }

        var stopwatch = Stopwatch.StartNew();
        handle.SetState(start.ContextTokens > 0 ? AiActivityState.PreparingContext : AiActivityState.Running);

        try
        {
            var result = await call(handle.Token);
            handle.Complete();
            Record(start, handle.Snapshot, inputCharacters, outputCharacters(result), stopwatch.ElapsedMilliseconds, AiUsageOutcome.Succeeded);
            return result;
        }
        catch (OperationCanceledException) when (handle.Token.IsCancellationRequested)
        {
            handle.MarkCancelled();
            Record(start, handle.Snapshot, inputCharacters, 0, stopwatch.ElapsedMilliseconds, AiUsageOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            handle.Fail(exception.Message);
            Record(start, handle.Snapshot, inputCharacters, 0, stopwatch.ElapsedMilliseconds, AiUsageOutcome.Failed);
            throw;
        }
    }

    private void Record(
        AiActivityStart start,
        AiActivitySnapshot snapshot,
        int inputCharacters,
        int outputCharacters,
        long durationMs,
        AiUsageOutcome outcome)
    {
        var exact = snapshot.Usage is { Estimated: false } ? snapshot.Usage : null;
        var reported = snapshot.Usage;

        // Estimates are only made for successful requests; the token cost of a failed or cancelled
        // request is unknown unless the provider reported it.
        var estimate = outcome == AiUsageOutcome.Succeeded && reported is null;
        var inputTokens = reported?.InputTokens ?? (estimate ? AiUsageTracker.EstimateTokens(inputCharacters) : 0);
        var outputTokens = reported?.OutputTokens ?? (estimate ? AiUsageTracker.EstimateTokens(outputCharacters) : 0);

        usage.Record(
            start.ProfileId,
            new AiUsageMeasurement(
                time.GetUtcNow(),
                start.Operation,
                start.ProviderId,
                snapshot.Model,
                inputCharacters,
                outputCharacters,
                (int)Math.Min(int.MaxValue, inputTokens),
                (int)Math.Min(int.MaxValue, outputTokens),
                Estimated: exact is null && (estimate || reported is { Estimated: true }),
                CacheHit: false,
                ResumedChunk: false)
            {
                CachedInputTokens = (int)Math.Min(int.MaxValue, exact?.CachedInputTokens ?? 0),
                ReasoningOutputTokens = (int)Math.Min(int.MaxValue, exact?.ReasoningOutputTokens ?? 0),
                ContextTokens = start.ContextTokens,
                FullContextTokens = Math.Max(start.ContextTokens, start.FullContextTokens),
                DurationMs = durationMs,
                Retries = snapshot.Retries,
                Outcome = outcome
            });
    }
}
