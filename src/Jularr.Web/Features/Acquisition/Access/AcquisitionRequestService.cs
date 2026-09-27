using Jularr.Web.Features.Auth;

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
    ILogger<AcquisitionRequestService> logger)
{
    public async Task<AcquisitionCapabilities> GetCapabilitiesAsync(
        MediaAcquisitionKind kind,
        CancellationToken cancellationToken) =>
        AcquisitionCapabilities.Resolve(await store.GetPolicyAsync(kind, cancellationToken), account.IsOwner);

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
        RequireOwner();
        var request = await RequireAsync(id, cancellationToken);
        // Approved requests that wait for a release can be searched again right away.
        if (request.Status is not (AcquisitionRequestStatus.Pending or AcquisitionRequestStatus.Failed or AcquisitionRequestStatus.Approved))
        {
            return request;
        }

        await store.UpdateStatusAsync(id, AcquisitionRequestStatus.Approved, null, null, null, account.ProfileId, cancellationToken);
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
        RequireOwner();
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
    }

    /// <summary>For media types without automatic acquisition: the owner added it by hand.</summary>
    public async Task MarkCompletedAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireOwner();
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

        if (!account.IsOwner && request.RequestedByProfileId != account.ProfileId)
        {
            throw new AcquisitionAccessDeniedException("Only the requester or the owner can withdraw a request.");
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
        return await RequireAsync(request.Id, cancellationToken);
    }

    private async Task<AcquisitionRequest> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken)
        ?? throw new InvalidOperationException("The request no longer exists.");

    private void RequireOwner()
    {
        if (!account.IsOwner)
        {
            throw new AcquisitionAccessDeniedException("Only the owner can decide requests.");
        }
    }
}
