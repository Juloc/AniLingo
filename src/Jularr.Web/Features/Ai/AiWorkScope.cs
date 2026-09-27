namespace Jularr.Web.Features.Ai;

/// <summary>
/// Where one AI request sits inside a longer job, set by the pipeline that issues it: its position
/// among the job's parts (for example segment 3 of 7 of a chapter) and the size of the shared story
/// context before it was compacted for this request (#412). Activities started inside the scope
/// carry both, so the live view shows progress and how much context the projection saved.
/// </summary>
public sealed record AiWorkContext(int? Part, int? Parts, int FullContextCharacters);

public static class AiWorkScope
{
    private static readonly AsyncLocal<AiWorkContext?> CurrentContext = new();

    public static AiWorkContext? Current => CurrentContext.Value;

    /// <param name="part">1-based position of the request; null when the job has no parts.</param>
    /// <param name="parts">Number of parts of the job.</param>
    /// <param name="fullContextCharacters">Shared context size before compaction; 0 when unknown.</param>
    public static IDisposable Enter(int? part, int? parts, int fullContextCharacters = 0)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = new AiWorkContext(
            part is > 0 && parts is > 0 ? Math.Min(part.Value, parts.Value) : null,
            part is > 0 && parts is > 0 ? parts : null,
            Math.Max(0, fullContextCharacters));
        return new Restore(previous);
    }

    /// <summary>Adds the ambient work context to an activity that is about to start.</summary>
    internal static AiActivityStart Apply(AiActivityStart start) =>
        Current is { FullContextCharacters: > 0 } work
            ? start with { FullContextTokens = AiUsageTracker.EstimateTokens(work.FullContextCharacters) }
            : start;

    private sealed class Restore(AiWorkContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
