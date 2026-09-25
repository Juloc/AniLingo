using System.Collections.Concurrent;

namespace AniLingo.Web.Features.Ai;

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
    bool ResumedChunk);

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

public sealed class AiUsageTracker
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
            if (!measurement.CacheHit && !measurement.ResumedChunk)
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
