using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Operations;

namespace Jularr.Web.Features.Acquisition.Wanted;

/// <summary>
/// Media-specific Wanted policy behind the shared scheduler. The scheduler owns timing and
/// download/import state transitions; handlers only interpret their target payload and tell
/// the canonical request service how to continue after a bad release.
/// </summary>
public interface IWantedRequestHandler
{
    MediaAcquisitionKind Kind { get; }

    bool IsSearchDue(
        AcquisitionRequest request,
        DateTime nowUtc);

    Task ContinueAfterProblemAsync(
        AcquisitionRequest request,
        string problem,
        CancellationToken cancellationToken);
}

/// <summary>
/// Generic durable Wanted lifecycle for request-backed media.
///
/// It deliberately does not talk to SABnzbd directly. The shared download monitor projects
/// queue/history state onto Operations; Wanted consumes that canonical state, transitions
/// requests through Downloading -> Importing -> Completed, dispatches imports once, and lets
/// the media handler continue with another release only when the downloaded package itself
/// is unsuitable.
/// </summary>
public sealed class WantedAcquisitionService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<WantedAcquisitionService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    public const int MaxRequestsPerKindPerPass = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await ProcessOnceAsync(
                    scope.ServiceProvider,
                    clock.GetUtcNow().UtcDateTime,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not advance Wanted acquisition requests.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public static async Task<int> ProcessOnceAsync(
        IServiceProvider services,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var handlers = services
            .GetServices<IWantedRequestHandler>()
            .GroupBy(handler => handler.Kind)
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException(
                        $"More than one Wanted handler is registered for {group.Key}."));

        var advanced = 0;
        foreach (var handler in handlers.Values)
        {
            advanced += await RecoverInFlightAsync(
                services,
                handler,
                cancellationToken);
            advanced += await SearchDueAsync(
                services,
                handler,
                nowUtc,
                cancellationToken);
        }

        return advanced;
    }

    private static async Task<int> RecoverInFlightAsync(
        IServiceProvider services,
        IWantedRequestHandler handler,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();
        var operations = new OperationStore(
            services.GetRequiredService<AppDbContext>());
        var dispatcher = services.GetRequiredService<CompletedDownloadDispatcher>();
        var locations = services.GetRequiredService<ICompletedDownloadLocationResolver>();

        var downloading = await store.ListByStatusAsync(
            handler.Kind,
            AcquisitionRequestStatus.Downloading,
            cancellationToken);
        var importing = await store.ListByStatusAsync(
            handler.Kind,
            AcquisitionRequestStatus.Importing,
            cancellationToken);

        var requests = downloading
            .Concat(importing)
            .GroupBy(request => request.Id)
            .Select(group => group.First())
            .OrderBy(request => request.UpdatedAt)
            .Take(MaxRequestsPerKindPerPass)
            .ToArray();

        var advanced = 0;
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var operation = request.OperationId is { } operationId
                ? await operations.GetAsync(operationId, cancellationToken)
                : null;

            if (operation is null)
            {
                await handler.ContinueAfterProblemAsync(
                    request,
                    "The download operation no longer exists.",
                    cancellationToken);
                advanced++;
                continue;
            }

            if (operation.Status is
                OperationStatus.Failed or
                OperationStatus.Cancelled or
                OperationStatus.Interrupted)
            {
                var problem = string.IsNullOrWhiteSpace(operation.Error)
                    ? "The download failed."
                    : $"The download failed: {operation.Error.Trim().TrimEnd('.')}.";
                await handler.ContinueAfterProblemAsync(
                    request,
                    problem,
                    cancellationToken);
                advanced++;
                continue;
            }

            if (operation.Status is OperationStatus.Queued or OperationStatus.Running)
            {
                if (request.Status == AcquisitionRequestStatus.Importing)
                {
                    await store.UpdateStatusAsync(
                        request.Id,
                        AcquisitionRequestStatus.Downloading,
                        "Download is still in progress.",
                        operation.Id,
                        resultUrl: null,
                        decidedByProfileId: null,
                        cancellationToken);
                    advanced++;
                }

                continue;
            }

            if (operation.Status != OperationStatus.Succeeded)
            {
                continue;
            }

            if (request.Status != AcquisitionRequestStatus.Importing)
            {
                await store.UpdateStatusAsync(
                    request.Id,
                    AcquisitionRequestStatus.Importing,
                    "Download complete. Importing into the library.",
                    operation.Id,
                    resultUrl: null,
                    decidedByProfileId: null,
                    cancellationToken);
                advanced++;
            }

            var location = await locations.ResolveAsync(
                operation,
                cancellationToken);
            if (!location.Resolved ||
                string.IsNullOrWhiteSpace(location.SourcePath))
            {
                await store.UpdateStatusAsync(
                    request.Id,
                    AcquisitionRequestStatus.Importing,
                    location.Message,
                    operation.Id,
                    resultUrl: null,
                    decidedByProfileId: null,
                    cancellationToken);
                continue;
            }

            var result = await dispatcher.DispatchAsync(
                new CompletedDownloadImportRequest(
                    request,
                    operation,
                    location.SourcePath),
                cancellationToken);

            switch (result.Disposition)
            {
                case CompletedDownloadImportDisposition.Completed:
                    await store.UpdateStatusAsync(
                        request.Id,
                        AcquisitionRequestStatus.Completed,
                        result.Message,
                        operation.Id,
                        result.ResultUrl,
                        decidedByProfileId: null,
                        cancellationToken);
                    advanced++;
                    break;

                case CompletedDownloadImportDisposition.RetryLater:
                    await store.UpdateStatusAsync(
                        request.Id,
                        AcquisitionRequestStatus.Importing,
                        result.Message,
                        operation.Id,
                        resultUrl: null,
                        decidedByProfileId: null,
                        cancellationToken);
                    break;

                case CompletedDownloadImportDisposition.RejectedRelease:
                    await handler.ContinueAfterProblemAsync(
                        request,
                        result.Message,
                        cancellationToken);
                    advanced++;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return advanced;
    }

    private static async Task<int> SearchDueAsync(
        IServiceProvider services,
        IWantedRequestHandler handler,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<AcquisitionAccessStore>();

        var due = (await store.ListByStatusAsync(
                handler.Kind,
                AcquisitionRequestStatus.Approved,
                cancellationToken))
            .Where(request => handler.IsSearchDue(request, nowUtc))
            .OrderBy(request => request.UpdatedAt)
            .Take(MaxRequestsPerKindPerPass)
            .ToArray();

        if (due.Length == 0)
        {
            return 0;
        }

        var requestService = services.GetRequiredService<AcquisitionRequestService>();
        foreach (var request in due)
        {
            await requestService.ContinueAsync(
                request.Id,
                cancellationToken);
        }

        return due.Length;
    }
}
