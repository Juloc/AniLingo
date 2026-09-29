using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Events;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// The one path for adding a title from search, for every media type: it applies the owner's
/// access policy, records the request and, when the profile may add automatically (or the owner
/// approves), hands it to the media type's executor.
/// </summary>
public sealed class AcquisitionRequestService(
    AcquisitionAccessStore store,
    IEnumerable<IAcquisitionRequestExecutor> executors,
    CurrentAccountContext account,
    IJularrEventPublisher events,
    ILogger<AcquisitionRequestService> logger)
{
    public async Task<AcquisitionCapabilities> GetCapabilitiesAsync(
        MediaAcquisitionKind kind,
        CancellationToken cancellationToken) =>
        AcquisitionCapabilities.Resolve(await store.GetPolicyAsync(kind, cancellationToken), account.Can(JularrPolicies.AdminMedia));

    /// <summary>Adds (or requests) a title. Returns the open request for it, new or existing.</summary>
    public async Task<AcquisitionRequest> SubmitAsync(
        AcquisitionRequestDraft draft,
        CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(draft.Kind, cancellationToken);
        if (!capabilities.CanAdd)
        {
            throw new AcquisitionAccessDeniedException("Adding this kind of media is reserved for the owner.");
        }

        if (await store.FindOpenAsync(draft.Kind, draft.Provider, draft.ExternalId, cancellationToken) is { } open)
        {
            return open;
        }

        if (capabilities.AddCreatesRequest)
        {
            return await store.CreateAsync(draft, account.ProfileId, AcquisitionRequestStatus.Pending, null, cancellationToken);
        }

        var approved = await store.CreateAsync(
            draft,
            account.ProfileId,
            AcquisitionRequestStatus.Approved,
            account.ProfileId,
            cancellationToken);
        return await ExecuteAsync(approved, cancellationToken);
    }

    public async Task<AcquisitionRequest> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireRequestManager();
        var request = await RequireAsync(id, cancellationToken);
        // Approved requests that wait for a release can be searched again right away.
        if (request.Status is not (AcquisitionRequestStatus.Pending or AcquisitionRequestStatus.Failed or AcquisitionRequestStatus.Approved))
        {
            return request;
        }

        await store.UpdateStatusAsync(id, AcquisitionRequestStatus.Approved, null, null, null, account.ProfileId, cancellationToken);
        await PublishDecisionAsync(request, JularrEventCategory.RequestApproved, cancellationToken);
        return await ExecuteAsync(await RequireAsync(id, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Runs an already approved request again without a signed-in owner: a background search for a
    /// title that had no release yet, or the next release after a failed download.
    /// </summary>
    public async Task<AcquisitionRequest> ContinueAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await RequireAsync(id, cancellationToken);
        if (request.Status is not (
                AcquisitionRequestStatus.Approved or
                AcquisitionRequestStatus.Downloading or
                AcquisitionRequestStatus.Importing))
        {
            return request;
        }

        return await ExecuteAsync(request, cancellationToken);
    }

    public async Task RejectAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        RequireRequestManager();
        var request = await RequireAsync(id, cancellationToken);
        if (request.Status != AcquisitionRequestStatus.Pending)
        {
            return;
        }

        await store.UpdateStatusAsync(
            id,
            AcquisitionRequestStatus.Rejected,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            null,
            null,
            account.ProfileId,
            cancellationToken);
        await PublishDecisionAsync(request, JularrEventCategory.RequestDenied, cancellationToken);
    }

    /// <summary>For media types without automatic acquisition: the owner added it by hand.</summary>
    public async Task MarkCompletedAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireRequestManager();
        var request = await RequireAsync(id, cancellationToken);
        if (!request.IsOpen)
        {
            return;
        }

        await store.UpdateStatusAsync(id, AcquisitionRequestStatus.Completed, null, null, null, account.ProfileId, cancellationToken);
    }

    /// <summary>A requester may withdraw their own pending request; the owner may withdraw any.</summary>
    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await RequireAsync(id, cancellationToken);
        if (request.Status != AcquisitionRequestStatus.Pending)
        {
            return;
        }

        if (!account.Can(JularrPolicies.AdminMedia) && request.RequestedByProfileId != account.ProfileId)
        {
            throw new AcquisitionAccessDeniedException("Only the requester, the owner or a media manager can withdraw a request.");
        }

        await store.UpdateStatusAsync(id, AcquisitionRequestStatus.Rejected, "Withdrawn.", null, null, account.ProfileId, cancellationToken);
    }

    private async Task<AcquisitionRequest> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken)
    {
        var executor = executors.FirstOrDefault(candidate => candidate.Kind == request.Kind);
        if (executor is null)
        {
            // No automatic acquisition for this media type yet: the approved request is the owner's to-do.
            await store.UpdateStatusAsync(
                request.Id,
                AcquisitionRequestStatus.Approved,
                "Approved — the owner adds it to the library.",
                null,
                null,
                null,
                cancellationToken);
            return await RequireAsync(request.Id, cancellationToken);
        }

        await store.UpdateStatusAsync(request.Id, AcquisitionRequestStatus.Searching, null, null, null, null, cancellationToken);
        AcquisitionExecution result;
        try
        {
            result = await executor.ExecuteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Acquisition request {RequestId} ({Kind}) failed.", request.Id, request.Kind);
            result = new AcquisitionExecution(AcquisitionRequestStatus.Failed, exception.Message);
        }

        await store.UpdateStatusAsync(
            request.Id,
            result.Status,
            result.Message,
            result.OperationId,
            result.ResultUrl,
            null,
            cancellationToken);

        // Every media kind's executor reports Downloading the moment it finds and grabs an
        // accepted release, so this one spot covers #579's "ReleaseAvailable when a wanted
        // release is found" for Anime, Manga, Light Novels and Books alike.
        if (result.Status == AcquisitionRequestStatus.Downloading)
        {
            await PublishReleaseAvailableAsync(request, result, cancellationToken);
        }

        return await RequireAsync(request.Id, cancellationToken);
    }

    /// <summary>
    /// Notifies the requester of the owner's decision (#429). Delivery is fully decoupled here:
    /// a channel failure inside <see cref="IJularrEventPublisher"/> is already caught and logged
    /// by the publisher, so it can never turn an approval/rejection into a failed request.
    /// </summary>
    private Task PublishDecisionAsync(AcquisitionRequest request, JularrEventCategory category, CancellationToken cancellationToken) =>
        events.PublishAsync(
            JularrEvent.Create(
                category,
                profileId: request.RequestedByProfileId,
                mediaType: AcquisitionAccessNames.Kind(request.Kind),
                subjectId: request.Id.ToString(),
                messageParams: new Dictionary<string, string> { ["title"] = request.Title },
                deepLink: request.ResultUrl,
                dedupKey: $"acquisition-request:{request.Id}:{category}"),
            cancellationToken);

    /// <summary>
    /// #579: tells the requester a release was found and grabbed for their request. Same dedup
    /// shape as <see cref="PublishDecisionAsync"/> keyed on the request, so a later release found
    /// after an earlier one failed refreshes one notification instead of piling up new rows.
    /// </summary>
    private Task PublishReleaseAvailableAsync(AcquisitionRequest request, AcquisitionExecution result, CancellationToken cancellationToken) =>
        events.PublishAsync(
            JularrEvent.Create(
                JularrEventCategory.ReleaseAvailable,
                profileId: request.RequestedByProfileId,
                mediaType: AcquisitionAccessNames.Kind(request.Kind),
                subjectId: request.Id.ToString(),
                messageParams: new Dictionary<string, string> { ["title"] = request.Title },
                deepLink: result.ResultUrl,
                dedupKey: $"acquisition-request:{request.Id}:release-available",
                relatedOperationId: result.OperationId),
            cancellationToken);

    private async Task<AcquisitionRequest> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken)
        ?? throw new InvalidOperationException("The request no longer exists.");

    private void RequireRequestManager()
    {
        if (!account.Can(JularrPolicies.AdminMedia))
        {
            throw new AcquisitionAccessDeniedException("Only the owner or a media manager can decide requests.");
        }
    }
}
