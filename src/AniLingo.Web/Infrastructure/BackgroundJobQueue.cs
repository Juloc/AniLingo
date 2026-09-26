using System.Collections.Concurrent;
using System.Threading.Channels;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Operations;

namespace AniLingo.Web.Infrastructure;

internal sealed record QueuedBackgroundWork(Guid OperationId);

internal sealed class RuntimeBackgroundWork(
    Func<OperationExecutionContext, IServiceProvider, CancellationToken, Task> work,
    bool retryable)
{
    public Func<OperationExecutionContext, IServiceProvider, CancellationToken, Task> Work { get; } = work;
    public bool Retryable { get; } = retryable;
    public CancellationTokenSource Cancellation { get; private set; } = new();

    public void RenewCancellation()
    {
        Cancellation.Dispose();
        Cancellation = new CancellationTokenSource();
    }
}

public abstract class BackgroundJobQueueBase(
    IServiceScopeFactory scopeFactory)
{
    private readonly Channel<QueuedBackgroundWork> channel =
        Channel.CreateBounded<QueuedBackgroundWork>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    // Operations this process queued itself are never recovered as abandoned, even when a
    // producer queues before the worker has finished recovering the previous process.
    private readonly DateTime ownedSinceUtc = DateTime.UtcNow;

    private readonly ConcurrentDictionary<Guid, RuntimeBackgroundWork> runtime =
        new();

    protected abstract OperationDescriptor DefaultDescriptor { get; }

    protected abstract IReadOnlyList<OperationLane> RecoveryLanes { get; }

    protected virtual OperationDescriptor NormalizeDescriptor(
        OperationDescriptor descriptor) =>
        descriptor;

    public ValueTask<Guid> QueueAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        QueueAsync(
            DefaultDescriptor,
            (_, services, workerToken) => work(services, workerToken),
            cancellationToken);

    public ValueTask<Guid> QueueAsync(
        OperationDescriptor descriptor,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        QueueAsync(
            descriptor,
            (_, services, workerToken) => work(services, workerToken),
            cancellationToken);

    public async ValueTask<Guid> QueueAsync(
        OperationDescriptor descriptor,
        Func<OperationExecutionContext, IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(work);

        descriptor = NormalizeDescriptor(descriptor);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new OperationStore(db);
        var operationId = await store.CreateAsync(descriptor, cancellationToken);

        var runtimeWork = new RuntimeBackgroundWork(work, descriptor.Retryable);
        if (!runtime.TryAdd(operationId, runtimeWork))
        {
            await store.MarkFailedAsync(
                operationId,
                "Could not reserve runtime state for the queued operation.",
                cancellationToken);
            throw new InvalidOperationException(
                "Could not reserve runtime state for the queued operation.");
        }

        try
        {
            await channel.Writer.WriteAsync(
                new QueuedBackgroundWork(operationId),
                cancellationToken);
            return operationId;
        }
        catch
        {
            runtime.TryRemove(operationId, out _);
            await store.MarkCancelledAsync(
                operationId,
                "Operation was cancelled before it entered the worker queue.",
                CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> CancelAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (!runtime.TryGetValue(operationId, out var work))
        {
            return false;
        }

        work.Cancellation.Cancel();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new OperationStore(db);
        var operation = await store.GetAsync(operationId, cancellationToken);

        if (operation?.Status == OperationStatus.Queued)
        {
            await store.MarkCancelledAsync(
                operationId,
                "Cancelled by administrator.",
                cancellationToken);
        }

        return operation is not null;
    }

    public async Task<bool> RetryAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (!runtime.TryGetValue(operationId, out var work) || !work.Retryable)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new OperationStore(db);

        if (!await store.PrepareRetryAsync(operationId, cancellationToken))
        {
            return false;
        }

        work.RenewCancellation();
        await channel.Writer.WriteAsync(
            new QueuedBackgroundWork(operationId),
            cancellationToken);
        return true;
    }

    public bool HasRuntimeWork(Guid operationId) =>
        runtime.ContainsKey(operationId);

    internal bool TryGetRuntimeWork(
        Guid operationId,
        out RuntimeBackgroundWork? work) =>
        runtime.TryGetValue(operationId, out work);

    internal void CompleteRuntimeWork(
        Guid operationId,
        bool keepForRetry)
    {
        if (keepForRetry)
        {
            return;
        }

        if (runtime.TryRemove(operationId, out var removed))
        {
            removed.Cancellation.Dispose();
        }
    }

    internal IAsyncEnumerable<QueuedBackgroundWork> ReadAllAsync(
        CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);

    internal async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new OperationStore(db);

        foreach (var lane in RecoveryLanes)
        {
            await store.RecoverInterruptedAsync(lane, ownedSinceUtc, cancellationToken);
        }
    }
}

public sealed class BackgroundJobQueue(IServiceScopeFactory scopeFactory)
    : BackgroundJobQueueBase(scopeFactory)
{
    protected override OperationDescriptor DefaultDescriptor =>
        OperationDescriptor.Background();

    protected override IReadOnlyList<OperationLane> RecoveryLanes =>
        [OperationLane.Normal, OperationLane.Maintenance];

    protected override OperationDescriptor NormalizeDescriptor(
        OperationDescriptor descriptor) =>
        descriptor.Lane == OperationLane.Interactive
            ? descriptor with { Lane = OperationLane.Normal }
            : descriptor;
}

public sealed class PlaybackJobQueue(IServiceScopeFactory scopeFactory)
    : BackgroundJobQueueBase(scopeFactory)
{
    protected override OperationDescriptor DefaultDescriptor =>
        OperationDescriptor.Playback();

    protected override IReadOnlyList<OperationLane> RecoveryLanes =>
        [OperationLane.Interactive];

    protected override OperationDescriptor NormalizeDescriptor(
        OperationDescriptor descriptor) =>
        descriptor with { Lane = OperationLane.Interactive };
}

public abstract class BackgroundJobWorkerBase<TQueue>(
    TQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger logger) : BackgroundService
    where TQueue : BackgroundJobQueueBase
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await queue.RecoverAsync(stoppingToken);

        await foreach (var queued in queue.ReadAllAsync(stoppingToken))
        {
            if (!queue.TryGetRuntimeWork(queued.OperationId, out var runtime)
                || runtime is null)
            {
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var store = new OperationStore(db);
            var operation = await store.GetAsync(
                queued.OperationId,
                stoppingToken);

            if (operation is null ||
                operation.Status is OperationStatus.Cancelled
                    or OperationStatus.Succeeded)
            {
                queue.CompleteRuntimeWork(
                    queued.OperationId,
                    keepForRetry: false);
                continue;
            }

            await store.MarkRunningAsync(
                queued.OperationId,
                stoppingToken);

            using var executionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    runtime.Cancellation.Token);

            var context = new OperationExecutionContext(
                queued.OperationId,
                scope.ServiceProvider);

            try
            {
                await runtime.Work(
                    context,
                    scope.ServiceProvider,
                    executionCancellation.Token);

                await store.MarkSucceededAsync(
                    queued.OperationId,
                    cancellationToken: CancellationToken.None);

                queue.CompleteRuntimeWork(
                    queued.OperationId,
                    keepForRetry: false);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                await store.MarkInterruptedAsync(
                    queued.OperationId,
                    "Interrupted because AniLingo is stopping.",
                    CancellationToken.None);
                break;
            }
            catch (OperationCanceledException)
                when (runtime.Cancellation.IsCancellationRequested)
            {
                await store.MarkCancelledAsync(
                    queued.OperationId,
                    "Cancelled by administrator.",
                    CancellationToken.None);
                queue.CompleteRuntimeWork(
                    queued.OperationId,
                    keepForRetry: runtime.Retryable);
            }
            catch (Exception exception)
            {
                var safeMessage =
                    $"{exception.GetType().Name}: {exception.Message}";
                await store.MarkFailedAsync(
                    queued.OperationId,
                    safeMessage,
                    CancellationToken.None);

                logger.LogError(
                    exception,
                    "Background operation {OperationId} failed.",
                    queued.OperationId);

                queue.CompleteRuntimeWork(
                    queued.OperationId,
                    keepForRetry: runtime.Retryable);
            }
        }
    }
}

public sealed class BackgroundJobWorker(
    BackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger)
    : BackgroundJobWorkerBase<BackgroundJobQueue>(
        queue,
        scopeFactory,
        logger)
{
}

public sealed class PlaybackJobWorker(
    PlaybackJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PlaybackJobWorker> logger)
    : BackgroundJobWorkerBase<PlaybackJobQueue>(
        queue,
        scopeFactory,
        logger)
{
}
