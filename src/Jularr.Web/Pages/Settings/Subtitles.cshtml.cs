using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Settings;

// The owner-only home for Bazarr-class subtitle language profiles (#526): create/edit profiles,
// assign them per media type and per library root, and see subtitle-provider status. Missing-
// subtitle diagnostics and the manual search UI stay on /Admin/Subtitles, which is the operational
// (not configuration) surface for subtitles.
[Authorize(Policy = JularrPolicies.AdminMedia)]
public sealed class SubtitlesModel(
    AppDbContext db,
    SubtitleLanguageProfileService profiles,
    SubtitleManualSearchService manualSearch,
    ILogger<SubtitlesModel> logger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public IReadOnlyList<SubtitleLanguageProfileDetail> Profiles { get; private set; } = [];
    public IReadOnlyList<LibraryRoot> Roots { get; private set; } = [];
    public IReadOnlyDictionary<WatchlistMediaType, Guid?> MediaTypeAssignments { get; private set; } =
        new Dictionary<WatchlistMediaType, Guid?>();
    public IReadOnlyDictionary<Guid, Guid?> LibraryRootAssignments { get; private set; } =
        new Dictionary<Guid, Guid?>();
    public bool IsExistingProfile { get; private set; }
    public bool HasProviders => manualSearch.HasProviders;
    public IReadOnlyList<string> ProviderNames => manualSearch.ProviderNames;

    public string? Notice => TempData["SubtitleSettingsNotice"] as string;

    /// <summary>
    /// Usually sourced from TempData after a redirect, but settable directly when redisplaying the
    /// page from a failed POST (<see cref="OnPostSaveAsync"/>) so the owner's just-typed form
    /// values are not lost on a round-trip through a fresh GET.
    /// </summary>
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Error = TempData["SubtitleSettingsError"] as string;
        await LoadAsync(cancellationToken);

        if (Edit is Guid id)
        {
            var detail = Profiles.FirstOrDefault(p => p.Id == id);
            if (detail is not null)
            {
                Input = ProfileInput.From(detail);
                IsExistingProfile = true;
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            var id = await profiles.UpsertAsync(
                Input.Id,
                Input.Name,
                Input.ParseItems(),
                Input.ParseCutoffPosition(),
                cancellationToken);

            TempData["SubtitleSettingsNotice"] = Ui.Format(
                "settings.subtitles.status.profileSaved", ("name", Input.Name));
            return RedirectToPage(new { edit = id });
        }
        catch (InvalidDataException exception)
        {
            // Redisplay the page directly (not a redirect) so the owner's just-typed name/items/
            // cutoff survive the validation error instead of being lost on a fresh GET. The
            // exception is logged and the reason shown through a localized template rather than
            // assigning the raw exception message to a status property (#523).
            logger.LogWarning(exception, "Subtitle profile save was rejected.");
            await LoadAsync(cancellationToken);
            IsExistingProfile = Input.Id is Guid;
            Error = Ui.Format("settings.subtitles.status.saveFailed", ("reason", exception.Message));
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid profileId, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var deleted = await profiles.DeleteAsync(profileId, cancellationToken);
        TempData[deleted ? "SubtitleSettingsNotice" : "SubtitleSettingsError"] = deleted
            ? Ui["settings.subtitles.status.profileDeleted"]
            : Ui["settings.subtitles.status.cannotDeleteProfile"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetDefaultAsync(Guid profileId, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await profiles.SetGlobalDefaultAsync(profileId, cancellationToken);
        TempData["SubtitleSettingsNotice"] = Ui["settings.subtitles.status.defaultUpdated"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignMediaTypeAsync(
        WatchlistMediaType mediaType,
        Guid? profileId,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await profiles.AssignMediaTypeAsync(mediaType, profileId, cancellationToken);
        TempData["SubtitleSettingsNotice"] = Ui["settings.subtitles.status.assignmentUpdated"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignLibraryRootAsync(
        Guid libraryRootId,
        Guid? profileId,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        await profiles.AssignLibraryRootAsync(libraryRootId, profileId, cancellationToken);
        TempData["SubtitleSettingsNotice"] = Ui["settings.subtitles.status.assignmentUpdated"];
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Profiles = await profiles.GetAllAsync(cancellationToken);
        Roots = await db.LibraryRoots.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);

        // Only the explicit per-scope override, not the resolved fallback, is shown as "assigned"
        // so the UI can distinguish an explicit choice from "currently following the default".
        var explicitAssignments = await db.SubtitleProfileAssignments.AsNoTracking().ToListAsync(cancellationToken);

        MediaTypeAssignments = Enum.GetValues<WatchlistMediaType>().ToDictionary(
            mediaType => mediaType,
            mediaType => explicitAssignments
                .Where(a => a.MediaType == mediaType && a.LibraryRootId == null)
                .Select(a => (Guid?)a.ProfileId)
                .SingleOrDefault());

        LibraryRootAssignments = Roots.ToDictionary(
            root => root.Id,
            root => explicitAssignments
                .Where(a => a.LibraryRootId == root.Id && a.MediaType == null)
                .Select(a => (Guid?)a.ProfileId)
                .SingleOrDefault());
    }

    public sealed class ProfileInput
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = "";
        public string ItemsText { get; set; } = "";
        public string? CutoffPositionText { get; set; }

        public static ProfileInput From(SubtitleLanguageProfileDetail detail) => new()
        {
            Id = detail.Id,
            Name = detail.Name,
            ItemsText = string.Join('\n', detail.Items.Select(FormatItem)),
            CutoffPositionText = detail.CutoffPosition?.ToString()
        };

        public IReadOnlyList<SubtitleLanguageProfileItemInput> ParseItems() =>
            (ItemsText ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line =>
                {
                    var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var languageTag = parts.Length > 0 ? parts[0] : "";
                    var forced = parts.Skip(1).Any(p => p.Equals("forced", StringComparison.OrdinalIgnoreCase));
                    var sdh = parts.Skip(1).Any(p => p.Equals("sdh", StringComparison.OrdinalIgnoreCase));
                    return new SubtitleLanguageProfileItemInput(languageTag, forced, sdh);
                })
                .ToArray();

        public int? ParseCutoffPosition() =>
            int.TryParse(CutoffPositionText, out var value) ? value : null;

        private static string FormatItem(SubtitleLanguageProfileItem item)
        {
            var flags = new List<string>();
            if (item.Forced)
            {
                flags.Add("forced");
            }

            if (item.Sdh)
            {
                flags.Add("sdh");
            }

            return flags.Count == 0 ? item.LanguageTag : $"{item.LanguageTag},{string.Join(',', flags)}";
        }
    }
}
