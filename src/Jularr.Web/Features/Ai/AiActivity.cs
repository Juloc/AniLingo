namespace Jularr.Web.Features.Ai;

public enum AiActivityState
{
    Queued,
    PreparingContext,
    Running,
    Retrying,
    Completed,
    Failed,
    Cancelled
}

public sealed record AiTokenUsage(
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    bool Estimated);

/// <summary>
/// Operational state of one AI request. Only phases, token counts and sanitized errors are kept —
/// never prompts, responses or reasoning text.
/// </summary>
public sealed record AiActivitySnapshot(
    Guid Id,
    string ProfileId,
    string Operation,
    AiActivityState State,
    string ProviderId,
    string? Transport,
    string? Model,
    string? ReasoningEffort,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ProgressCurrent,
    int? ProgressTotal,
    AiTokenUsage? Usage,
    long? ContextWindow,
    int SourceTokens,
    int ContextTokens,
    int Retries,
    string? Error)
{
    public bool IsActive => State is AiActivityState.Queued
        or AiActivityState.PreparingContext
        or AiActivityState.Running
        or AiActivityState.Retrying;

    public TimeSpan Elapsed(DateTimeOffset now) =>
        (CompletedAt ?? now) - StartedAt;

    /// <summary>Share of the model context window used by the input, when both are known.</summary>
    public double? ContextWindowPercent =>
        Usage is { Estimated: false } && ContextWindow is > 0
            ? Math.Min(100, Usage.InputTokens * 100d / ContextWindow.Value)
            : null;
}

public sealed record AiActivityStart(
    string ProfileId,
    string Operation,
    string ProviderId,
    AiInvocationOptions Options,
    int SourceTokens = 0,
    int ContextTokens = 0);

public enum AiActivityCancelResult
{
    Cancelled,
    NotFound,
    Forbidden,
    NotActive
}

/// <summary>Ambient handle of the AI request running on the current async flow.</summary>
public static class AiActivityScope
{
    private static readonly AsyncLocal<AiActivityHandle?> CurrentHandle = new();

    public static AiActivityHandle? Current => CurrentHandle.Value;

    public static IDisposable Enter(AiActivityHandle handle)
    {
        var previous = CurrentHandle.Value;
        CurrentHandle.Value = handle;
        return new Restore(previous);
    }

    private sealed class Restore(AiActivityHandle? previous) : IDisposable
    {
        public void Dispose() => CurrentHandle.Value = previous;
    }
}

public sealed class AiActivityHandle : IDisposable
{
    private readonly AiActivityTracker tracker;
    private readonly object sync = new();
    private readonly CancellationTokenSource cancellation;
    private AiActivitySnapshot snapshot;

    internal AiActivityHandle(
        AiActivityTracker tracker,
        AiActivitySnapshot snapshot,
        AiInvocationOptions options,
        CancellationToken requestToken)
    {
        this.tracker = tracker;
        this.snapshot = snapshot;
        Options = options;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
    }

    public Guid Id => snapshot.Id;

    public AiInvocationOptions Options { get; }

    /// <summary>Cancelled by the caller's token or by an explicit cancel from the activity view.</summary>
    public CancellationToken Token => cancellation.Token;

    public AiActivitySnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return snapshot;
            }
        }
    }

    public void SetState(AiActivityState state) =>
        Update(x => x.IsActive ? x with { State = state } : x);

    public void SetTransport(string transport) =>
        Update(x => x with { Transport = transport });

    public void SetModel(string? model, string? reasoningEffort) =>
        Update(x => x with
        {
            Model = string.IsNullOrWhiteSpace(model) ? x.Model : model,
            ReasoningEffort = string.IsNullOrWhiteSpace(reasoningEffort) ? x.ReasoningEffort : reasoningEffort
        });

    public void ReportUsage(AiTokenUsage usage, long? contextWindow = null) =>
        Update(x => x with { Usage = usage, ContextWindow = contextWindow ?? x.ContextWindow });

    public void ReportProgress(int current, int total) =>
        Update(x => x with { ProgressCurrent = Math.Max(0, current), ProgressTotal = Math.Max(0, total) });

    public void ReportRetry() =>
        Update(x => x with { Retries = x.Retries + 1, State = AiActivityState.Retrying });

    public void Complete() => Finish(AiActivityState.Completed, null);

    public void Fail(string? error) => Finish(AiActivityState.Failed, AiErrorSanitizer.Sanitize(error));

    public void MarkCancelled() => Finish(AiActivityState.Cancelled, null);

    internal void RequestCancel() => cancellation.Cancel();

    private void Finish(AiActivityState state, string? error) =>
        Update(x => x.IsActive
            ? x with { State = state, Error = error, CompletedAt = tracker.Now }
            : x);

    private void Update(Func<AiActivitySnapshot, AiActivitySnapshot> change)
    {
        AiActivitySnapshot next;
        lock (sync)
        {
            next = change(snapshot);
            if (next == snapshot)
            {
                return;
            }

            snapshot = next;
        }

        tracker.Publish(this);
    }

    public void Dispose()
    {
        if (Snapshot.IsActive)
        {
            Fail(null);
        }

        cancellation.Dispose();
    }
}

/// <summary>
/// In-memory registry of active and recently finished AI requests with change notifications
/// for the live activity stream. Durable history lives in <see cref="AiUsageStore"/>.
/// </summary>
public sealed class AiActivityTracker(TimeProvider time)
{
    private const int MaxFinished = 60;
    private readonly object gate = new();
    private readonly Dictionary<Guid, AiActivityHandle> active = [];
    private readonly LinkedList<AiActivitySnapshot> finished = new();
    private TaskCompletionSource changed = NewSignal();
    private long version;

    internal DateTimeOffset Now => time.GetUtcNow();

    public long Version => Interlocked.Read(ref version);

    public AiActivityHandle Start(AiActivityStart start, CancellationToken requestToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start.ProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(start.Operation);

        var handle = new AiActivityHandle(
            this,
            new AiActivitySnapshot(
                Guid.NewGuid(),
                start.ProfileId,
                start.Operation,
                AiActivityState.Queued,
                start.ProviderId,
                null,
                start.Options.Model,
                start.Options.ReasoningEffort,
                Now,
                null,
                null,
                null,
                null,
                null,
                start.SourceTokens,
                start.ContextTokens,
                0,
                null),
            start.Options,
            requestToken);

        lock (gate)
        {
            active[handle.Id] = handle;
        }

        Signal();
        return handle;
    }

    /// <summary>Active requests first, then recently finished ones; all profiles when null.</summary>
    public IReadOnlyList<AiActivitySnapshot> List(string? profileId)
    {
        lock (gate)
        {
            return active.Values
                .Select(x => x.Snapshot)
                .OrderBy(x => x.StartedAt)
                .Concat(finished)
                .Where(x => profileId is null || string.Equals(x.ProfileId, profileId, StringComparison.Ordinal))
                .ToArray();
        }
    }

    public AiActivityCancelResult Cancel(Guid id, string profileId, bool isOwner)
    {
        AiActivityHandle? handle;
        lock (gate)
        {
            active.TryGetValue(id, out handle);
            if (handle is null)
            {
                return finished.Any(x => x.Id == id && (isOwner || x.ProfileId == profileId))
                    ? AiActivityCancelResult.NotActive
                    : AiActivityCancelResult.NotFound;
            }
        }

        var snapshot = handle.Snapshot;
        if (!isOwner && !string.Equals(snapshot.ProfileId, profileId, StringComparison.Ordinal))
        {
            // Other profiles' requests are not revealed to non-owners.
            return AiActivityCancelResult.NotFound;
        }

        if (!snapshot.IsActive)
        {
            return AiActivityCancelResult.NotActive;
        }

        handle.RequestCancel();
        return AiActivityCancelResult.Cancelled;
    }

    /// <summary>Completes after the next change, or when <paramref name="cancellationToken"/> fires.</summary>
    public Task WaitForChangeAsync(long sinceVersion, CancellationToken cancellationToken)
    {
        Task signal;
        lock (gate)
        {
            if (Version != sinceVersion)
            {
                return Task.CompletedTask;
            }

            signal = changed.Task;
        }

        return signal.WaitAsync(cancellationToken);
    }

    internal void Publish(AiActivityHandle handle)
    {
        var snapshot = handle.Snapshot;
        if (!snapshot.IsActive)
        {
            lock (gate)
            {
                if (active.Remove(snapshot.Id))
                {
                    finished.AddFirst(snapshot);
                    while (finished.Count > MaxFinished)
                    {
                        finished.RemoveLast();
                    }
                }
            }
        }

        Signal();
    }

    private void Signal()
    {
        TaskCompletionSource previous;
        lock (gate)
        {
            Interlocked.Increment(ref version);
            previous = changed;
            changed = NewSignal();
        }

        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
