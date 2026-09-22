using System.Threading.Channels;

namespace AniLingo.Web.Infrastructure;

public sealed class BackgroundJobQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> channel =
        Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public ValueTask QueueAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        channel.Writer.WriteAsync(work, cancellationToken);

    internal IAsyncEnumerable<Func<IServiceProvider, CancellationToken, Task>> ReadAllAsync(
        CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class BackgroundJobWorker(
    BackgroundJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await work(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job failed.");
            }
        }
    }
}
