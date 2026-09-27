using System.Collections.Concurrent;

namespace Jularr.Web.Features.Ai;

public enum AiUsageOutcome
{
    Succeeded,
    Failed,
    Cancelled
}

public sealed record AiUsageMeasurement(
    DateTimeOffset Timestamp,
    string Operation,
    string ProviderId,
    string? Model,
    int InputCharacters,
    int OutputCharacters,
    int InputTokens,
    int OutputTokens,
    bool Estimated,
    bool CacheHit,
    bool ResumedChunk)
{
    /// <summary>Input tokens served from the provider's prompt cache (exact values only).</summary>
    public int CachedInputTokens { get; init; }

    /// <summary>Reasoning output tokens when the provider reports them; never the reasoning text.</summary>
    public int ReasoningOutputTokens { get; init; }

    /// <summary>Estimated share of the input that came from shared story/translation context (#412).</summary>
    public int ContextTokens { get; init; }

    public long DurationMs { get; init; }

    public int Retries { get; init; }

    public AiUsageOutcome Outcome { get; init; } = AiUsageOutcome.Succeeded;

    public bool IsRequest => !CacheHit && !ResumedChunk;
}

public sealed record AiUsageSnapshot(
    int RequestCount,
    long InputTokens,
    long OutputTokens,
    int CacheHits,
    int ResumedChunks,
    IReadOnlyList<AiUsageMeasurement> Recent);

public interface IAiUsageReporter
{
    void RecordCacheHit(string operation);
    void RecordResumedChunk(string operation);
}

/// <summary>Receives every measurement for durable aggregation.</summary>
public interface IAiUsageSink
{
    void Enqueue(string profileId, AiUsageMeasurement measurement);
}

/// <summary>
/// Records AI usage per profile. Recent measurements stay in memory for the live view; every
/// measurement is also handed to the durable sink so daily aggregates survive restarts.
/// </summary>
public sealed class AiUsageTracker(IAiUsageSink? sink = null)
{
    private const int MaxRecentPerProfile = 50;
    private readonly ConcurrentDictionary<string, ProfileUsage> profiles =
        new(StringComparer.Ordinal);

    public void Record(
        string profileId,
        AiUsageMeasurement measurement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(measurement);

        var usage = profiles.GetOrAdd(
            profileId,
            _ => new ProfileUsage());

        lock (usage.Gate)
        {
            if (measurement.IsRequest)
            {
                usage.RequestCount++;
                usage.InputTokens += measurement.InputTokens;
                usage.OutputTokens += measurement.OutputTokens;
            }

            if (measurement.CacheHit)
            {
                usage.CacheHits++;
            }

            if (measurement.ResumedChunk)
            {
                usage.ResumedChunks++;
            }

            usage.Recent.AddFirst(measurement);
            while (usage.Recent.Count > MaxRecentPerProfile)
            {
                usage.Recent.RemoveLast();
            }
        }

        sink?.Enqueue(profileId, measurement);
    }

    public AiUsageSnapshot GetSnapshot(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (!profiles.TryGetValue(profileId, out var usage))
        {
            return new AiUsageSnapshot(0, 0, 0, 0, 0, []);
        }

        lock (usage.Gate)
        {
            return new AiUsageSnapshot(
                usage.RequestCount,
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheHits,
                usage.ResumedChunks,
                usage.Recent.ToArray());
        }
    }

    public static int EstimateTokens(int characters) =>
        characters <= 0
            ? 0
            : Math.Max(1, (characters + 3) / 4);

    private sealed class ProfileUsage
    {
        public object Gate { get; } = new();
        public int RequestCount { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public int CacheHits { get; set; }
        public int ResumedChunks { get; set; }
        public LinkedList<AiUsageMeasurement> Recent { get; } = new();
    }
}
