using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Quality;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Requests;

/// <summary>
/// The signed-in profile's own request history (#597): everything it asked for and where each request
/// stands. Every query is scoped to the current profile, including for the owner; the owner's queue of
/// all requests is Admin → Requests.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    RequestHistoryQuery history,
    AcquisitionRequestService requests,
    QualityProfileStore qualityProfiles) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public RequestHistoryPage History { get; private set; } = new([], RequestHistoryFilter.All, 1, 0, 0);
    public IReadOnlyDictionary<string, string> QualityProfileNames { get; private set; } = new Dictionary<string, string>();

    /// <summary>The requester's chosen options as short lines under the title.</summary>
    public IReadOnlyList<string> OptionsOf(AcquisitionRequest request) =>
        RequestOptionsSummary.Describe(request.Options, Ui, QualityProfileNames);

    public async Task OnGetAsync(string? show, int p, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        History = await history.GetAsync(
            account.ProfileId,
            RequestHistoryQuery.ParseFilter(show),
            p,
            cancellationToken);
        QualityProfileNames = (await qualityProfiles.LoadAsync(cancellationToken)).Profiles
            .ToDictionary(profile => profile.Id, profile => profile.Name, StringComparer.Ordinal);
    }

    /// <summary>A requester withdraws a request that still waits for a decision.</summary>
    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, string? show, int p, CancellationToken cancellationToken)
    {
        try
        {
            await requests.CancelAsync(id, cancellationToken);
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }

        return RedirectToPage(new { show, p });
    }
}
