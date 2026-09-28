using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Admin;

/// <summary>Owner page: who may add what (per media type) and the request queue from all users.</summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class RequestsModel(
    AppDbContext db,
    AcquisitionAccessStore store,
    AcquisitionRequestService requests) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<AcquisitionAccessPolicy> Policies { get; private set; } = [];
    public IReadOnlyList<AcquisitionRequest> Requests { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ProfileNames { get; private set; } = new Dictionary<string, string>();
    public bool ShowAll { get; private set; }

    public async Task OnGetAsync(bool all, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        ShowAll = all;
        Policies = await store.GetPoliciesAsync(cancellationToken);
        Requests = await store.ListAsync(null, null, openOnly: !all, limit: 200, cancellationToken);
        ProfileNames = await db.OwnerAccounts
            .AsNoTracking()
            .ToDictionaryAsync(account => account.Id, account => account.UserName, cancellationToken);
    }

    public async Task<IActionResult> OnPostPoliciesAsync(CancellationToken cancellationToken)
    {
        if (!JularrPolicies.Allows(User, JularrPolicies.AcquisitionSettings))
        {
            return Forbid();
        }

        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            var name = AcquisitionAccessNames.Kind(kind);
            var userAdd = Request.Form[$"userAdd.{name}"].ToString();
            var manual = Request.Form[$"manual.{name}"].ToString();
            if (userAdd.Length == 0 || manual.Length == 0)
            {
                continue;
            }

            await store.SavePolicyAsync(
                new AcquisitionAccessPolicy(
                    kind,
                    AcquisitionAccessNames.ParseUserAdd(userAdd),
                    AcquisitionAccessNames.ParseManual(manual)),
                cancellationToken);
        }

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = ui["admin.requests.policiesSaved"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await requests.ApproveAsync(id, cancellationToken);
        TempData["Status"] = request.StatusMessage ?? request.Title;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        await requests.RejectAsync(id, note, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await requests.MarkCompletedAsync(id, cancellationToken);
        return RedirectToPage();
    }
}
