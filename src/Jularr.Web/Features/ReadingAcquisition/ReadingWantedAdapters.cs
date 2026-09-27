using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Wanted;

namespace Jularr.Web.Features.ReadingAcquisition;

/// <summary>Manga and Light Novel requests on the shared release-request Wanted policy.</summary>
public abstract class ReadingWantedRequestHandler(
    AcquisitionAccessStore store,
    AcquisitionRequestService requests) : ReleaseRequestWantedHandler(store, requests)
{
    protected override ReleaseRequestPayload ReadPayload(
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
