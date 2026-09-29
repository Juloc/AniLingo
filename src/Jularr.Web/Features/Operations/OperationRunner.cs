using Jularr.Web.Data;
using Jularr.Web.Features.Events;

namespace Jularr.Web.Features.Operations;

public sealed class OperationExecutionContext(
    Guid operationId,
    IServiceProvider services)
{
    public Guid OperationId { get; } = operationId;

    public Task ReportAsync(
        int? percent,
        string? message = null,
        long? bytesCompleted = null,
        long? bytesTotal = null,
        double? bytesPerSecond = null,
        DateTime? etaUtc = null,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        return new OperationStore(db).ReportProgressAsync(
            OperationId,
            percent,
            message,
            bytesCompleted,
            bytesTotal,
            bytesPerSecond,
            etaUtc,
            cancellationToken);
    }

    public Task LogAsync(
        OperationLogLevel level,
        string module,
        string message,
        CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        return new OperationStore(db).AppendLogAsync(
            OperationId,
            level,
            module,
            message,
            cancellationToken);
    }
}

/// <summary>
/// <paramref name="events"/> is optional (defaults to null) so the many tests that construct this
/// runner directly with just (db, services) keep compiling; DI resolves the real publisher for
/// every production caller, so #579's DownloadGrabbed/Failed and ImportCompleted/Failed events
/// (published from <see cref="OperationStore.MarkSucceededAsync"/>/<see cref="OperationStore.MarkFailedAsync"/>)
/// fire for every operation kind this runner drives — for example the Manga/Light Novel/Book inbox
/// import and the Light Novel EPUB upload import — without each of those callers wiring anything.
/// </summary>
public sealed class OperationRunner(
    AppDbContext db,
    IServiceProvider services,
    IJularrEventPublisher? events = null)
{
    public async Task RunAsync(
        OperationDescriptor descriptor,
        Func<OperationExecutionContext, CancellationToken, Task> action,
        string? successMessage = null,
        CancellationToken cancellationToken = default)
    {
        await RunAsync<object?>(
            descriptor,
            async (operation, token) =>
            {
                await action(operation, token);
                return null;
            },
            successMessage,
            cancellationToken);
    }

    public async Task<T> RunAsync<T>(
        OperationDescriptor descriptor,
        Func<OperationExecutionContext, CancellationToken, Task<T>> action,
        string? successMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(action);

        var store = new OperationStore(db, events);
        var operationId = await store.CreateAsync(
            descriptor,
            cancellationToken);

        await store.MarkRunningAsync(
            operationId,
            cancellationToken);

        var context = new OperationExecutionContext(
            operationId,
            services);

        try
        {
            var result = await action(
                context,
                cancellationToken);

            await store.MarkSucceededAsync(
                operationId,
                successMessage,
                CancellationToken.None);

            return result;
        }
        catch (OperationCanceledException)
        {
            await store.MarkCancelledAsync(
                operationId,
                "Operation cancelled before completion.",
                CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await store.MarkFailedAsync(
                operationId,
                $"{exception.GetType().Name}: {exception.Message}",
                CancellationToken.None);
            throw;
        }
    }
}
