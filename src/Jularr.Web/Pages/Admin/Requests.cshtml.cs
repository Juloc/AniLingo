using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Quality;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Admin;

/// <summary>
/// Owner page: the request queue from all users, who may use the manual add tools, the auto-approval
/// rules and the quality profiles requesters may pick. Who may request or add at once is not set here;
/// that is the capability matrix (Admin → Media capabilities).
/// </summary>
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class RequestsModel(
    AppDbContext db,
    AcquisitionAccessStore store,
    AcquisitionRequestService requests,
    AcquisitionRequestSettingsStore settings,
    AnimeQualityProfileStore qualityProfiles) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<AcquisitionAccessPolicy> Policies { get; private set; } = [];
    public IReadOnlyList<AcquisitionRequest> Requests { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ProfileNames { get; private set; } = new Dictionary<string, string>();
    public AcquisitionRequestSettings RequestSettings { get; private set; } = AcquisitionRequestSettings.Default;
    public IReadOnlyList<AnimeQualityProfile> QualityProfiles { get; private set; } = [];
    public IReadOnlyDictionary<string, string> QualityProfileNames { get; private set; } = new Dictionary<string, string>();
    public bool ShowAll { get; private set; }

    /// <summary>Whether the signed-in account may change rules and settings (the queue itself needs only the page policy).</summary>
    public bool CanEditSettings => JularrPolicies.Allows(User, JularrPolicies.AcquisitionSettings);

    /// <summary>The requester's chosen options as short lines for the queue.</summary>
    public IReadOnlyList<string> OptionsOf(AcquisitionRequest request) =>
        RequestOptionsSummary.Describe(request.Options, Ui, QualityProfileNames);

    public async Task OnGetAsync(bool all, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        ShowAll = all;
        Policies = await store.GetPoliciesAsync(cancellationToken);
        Requests = await store.ListAsync(null, null, openOnly: !all, limit: 200, cancellationToken);
        ProfileNames = await db.OwnerAccounts
            .AsNoTracking()
            .ToDictionaryAsync(account => account.Id, account => account.UserName, cancellationToken);
        RequestSettings = await settings.LoadAsync(cancellationToken);
        QualityProfiles = (await qualityProfiles.LoadAsync(cancellationToken)).Profiles;
        QualityProfileNames = QualityProfiles.ToDictionary(profile => profile.Id, profile => profile.Name, StringComparer.Ordinal);
    }

    public async Task<IActionResult> OnPostPoliciesAsync(CancellationToken cancellationToken)
    {
        if (!CanEditSettings)
        {
            return Forbid();
        }

        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            var manual = Request.Form[$"manual.{AcquisitionAccessNames.Kind(kind)}"].ToString();
            if (manual.Length == 0)
            {
                continue;
            }

            await store.SavePolicyAsync(
                new AcquisitionAccessPolicy(kind, AcquisitionAccessNames.ParseManual(manual)),
                cancellationToken);
        }

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = ui["admin.requests.policiesSaved"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddRuleAsync(
        string? name,
        string[]? kinds,
        string? profileId,
        int? maxRequests,
        int? periodDays,
        CancellationToken cancellationToken)
    {
        if (!CanEditSettings)
        {
            return Forbid();
        }

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        try
        {
            // The limit needs both numbers or neither; a half-filled limit is a mistake, not "no limit".
            var quota = maxRequests is null && periodDays is null
                ? null
                : new AutoApprovalQuota(maxRequests ?? 0, periodDays ?? 0);
            var rule = AutoApprovalRule.Create(
                name,
                (kinds ?? []).Select(AcquisitionAccessNames.ParseKind),
                string.IsNullOrWhiteSpace(profileId) ? [] : [profileId],
                quota);
            await settings.AddRuleAsync(rule, cancellationToken);
            TempData["Status"] = ui["admin.requests.autoSaved"];
        }
        catch (ArgumentException)
        {
            TempData["Status"] = ui["admin.requests.autoInvalid"];
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRuleEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        if (!CanEditSettings)
        {
            return Forbid();
        }

        await settings.SetRuleEnabledAsync(id, enabled, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveRuleAsync(string id, CancellationToken cancellationToken)
    {
        if (!CanEditSettings)
        {
            return Forbid();
        }

        await settings.RemoveRuleAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostProfilesAsync(string[]? profiles, CancellationToken cancellationToken)
    {
        if (!CanEditSettings)
        {
            return Forbid();
        }

        // Only profiles that exist can be opened to requests.
        var known = (await qualityProfiles.LoadAsync(cancellationToken)).Profiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.Ordinal);
        await settings.SetRequesterQualityProfilesAsync((profiles ?? []).Where(known.Contains), cancellationToken);

        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = ui["admin.requests.profilesSaved"];
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
