using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Wanted;

namespace Jularr.Web.Features.ReadingAcquisition;

public abstract class ReadingWantedRequestHandler(
    AcquisitionAccessStore store,
    AcquisitionRequestService requests) : IWantedRequestHandler
{
    public abstract MediaAcquisitionKind Kind { get; }

    public bool IsSearchDue(
        AcquisitionRequest request,
        DateTime nowUtc)
    {
        var payload = Payload(request);
        return payload.NextSearchUtc is { } next
            ? next <= nowUtc
            : payload.Searches == 0;
    }

    public async Task ContinueAfterProblemAsync(
        AcquisitionRequest request,
        string problem,
        CancellationToken cancellationToken)
    {
        var payload = Payload(request) with
        {
            LastProblem = string.IsNullOrWhiteSpace(problem)
                ? "The previous release could not be used."
                : problem.Trim(),
            NextSearchUtc = null
        };

        await store.UpdatePayloadAsync(
            request.Id,
            JsonSerializer.Serialize(
                payload,
                JsonSerializerOptions.Web),
            cancellationToken);

        await requests.ContinueAsync(
            request.Id,
            cancellationToken);
    }

    private ReadingRequestPayload Payload(
        AcquisitionRequest request)
    {
        var fallback = Kind == MediaAcquisitionKind.Manga
            ? new ReadingAcquisitionTarget(
                Kind,
                request.Title,
                string.IsNullOrWhiteSpace(request.Subtitle)
                    ? []
                    : [request.Subtitle])
            : new ReadingAcquisitionTarget(
                Kind,
                request.Title,
                [],
                request.Subtitle);

        return ReadingAcquisitionEngine.ReadPayload(
            request,
            fallback);
    }
}

public sealed class MangaWantedRequestHandler(
    AcquisitionAccessStore store,
    AcquisitionRequestService requests)
    : ReadingWantedRequestHandler(store, requests)
{
    public override MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.Manga;
}

public sealed class LightNovelWantedRequestHandler(
    AcquisitionAccessStore store,
    AcquisitionRequestService requests)
    : ReadingWantedRequestHandler(store, requests)
{
    public override MediaAcquisitionKind Kind =>
        MediaAcquisitionKind.LightNovel;
}
