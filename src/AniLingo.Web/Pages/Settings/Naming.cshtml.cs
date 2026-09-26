using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Naming;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Settings;

// The one place where the owner edits naming profiles, previews them against Sonarr's sample
// and chooses the default and per-library profile. Per-anime selection lives on /Library/Rename.
[Authorize(Roles = AccountRoles.Owner)]
public sealed class NamingModel(
    AnimeNamingProfileStore store,
    AppDbContext db) : PageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = ProfileInput.From(AnimeNamingPresets.SonarrDefault());

    [BindProperty(SupportsGet = true)]
    public string? Edit { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Preset { get; set; }

    public AnimeNamingState State { get; private set; } = AnimeNamingPresets.CreateDefaultState();
    public IReadOnlyList<LibraryRoot> Roots { get; private set; } = [];
    public bool IsExistingProfile { get; private set; }
    public IReadOnlyList<string> ValidationErrors { get; private set; } = [];
    public IReadOnlyList<AnimeNamingPreviewLine> Preview { get; private set; } = [];
    public string? Notice => TempData["NamingNotice"] as string;
    public string? Error => TempData["NamingError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        var existing = State.Profiles.FirstOrDefault(profile =>
            profile.Id.Equals(Edit ?? State.DefaultProfileId, StringComparison.OrdinalIgnoreCase));
        var preset = AnimeNamingPresets.All.FirstOrDefault(profile =>
            profile.Id.Equals(Preset, StringComparison.OrdinalIgnoreCase));

        if (preset is not null)
        {
            Input = ProfileInput.From(preset with { Id = UniqueId(preset.Id), Name = preset.Name });
            IsExistingProfile = false;
        }
        else if (existing is not null)
        {
            Input = ProfileInput.From(existing);
            IsExistingProfile = true;
        }

        BuildPreview();
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        IsExistingProfile = State.Profiles.Any(profile => profile.Id.Equals(Input.Id, StringComparison.OrdinalIgnoreCase));
        BuildPreview();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        IsExistingProfile = State.Profiles.Any(profile => profile.Id.Equals(Input.Id, StringComparison.OrdinalIgnoreCase));
        BuildPreview();
        if (ValidationErrors.Count > 0)
        {
            return Page();
        }

        var profile = Input.ToProfile();
        await store.UpsertAsync(profile, cancellationToken);
        TempData["NamingNotice"] = $"Saved naming profile '{profile.Name}'. Existing files keep their names until you rename them from an anime's rename page.";
        return RedirectToPage(new { edit = profile.Id });
    }

    public async Task<IActionResult> OnPostDefaultAsync(string profileId, CancellationToken cancellationToken)
    {
        return await RunAsync(
            () => store.SetDefaultAsync(profileId, cancellationToken),
            "Default naming profile updated.");
    }

    public async Task<IActionResult> OnPostLibraryAsync(Guid rootId, string? profileId, CancellationToken cancellationToken)
    {
        return await RunAsync(
            () => store.AssignLibraryAsync(rootId, profileId, cancellationToken),
            "Library naming profile updated.");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string profileId, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(profileId, cancellationToken);
        TempData[deleted ? "NamingNotice" : "NamingError"] = deleted
            ? "Naming profile deleted."
            : "A profile that is the default or assigned to a library or anime cannot be deleted.";
        return RedirectToPage();
    }

    public int UsageCount(string profileId) =>
        State.LibraryProfileAssignments.Values.Count(id => id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) +
        State.AnimeAssignments.Values.Count(assignment =>
            string.Equals(assignment.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

    private async Task<IActionResult> RunAsync(Func<Task> action, string notice)
    {
        try
        {
            await action();
            TempData["NamingNotice"] = notice;
        }
        catch (InvalidDataException exception)
        {
            TempData["NamingError"] = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        State = await store.LoadAsync(cancellationToken);
        Roots = await db.LibraryRoots.AsNoTracking().OrderBy(root => root.Name).ToListAsync(cancellationToken);
    }

    private void BuildPreview()
    {
        var profile = Input.ToProfile();
        ValidationErrors = AnimeNamingFormatter.Validate(profile);
        Preview = ValidationErrors.Count == 0 ? AnimeNamingSamples.Preview(profile) : [];
    }

    private string UniqueId(string baseId)
    {
        var id = baseId;
        for (var suffix = 2; State.Profiles.Any(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            id = $"{baseId}-{suffix}";
        }

        return id;
    }

    public sealed class ProfileInput
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string SeriesFolderFormat { get; set; } = "";
        public bool UseSeasonFolders { get; set; }
        public string SeasonFolderFormat { get; set; } = "";
        public string SpecialsFolderFormat { get; set; } = "";
        public string StandardEpisodeFormat { get; set; } = "";
        public string DailyEpisodeFormat { get; set; } = "";
        public string AnimeEpisodeFormat { get; set; } = "";
        public AnimeMultiEpisodeStyle MultiEpisodeStyle { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public AnimeColonReplacement ColonReplacement { get; set; }
        public string? CustomColonReplacement { get; set; }

        public static ProfileInput From(AnimeNamingProfile profile) =>
            new()
            {
                Id = profile.Id,
                Name = profile.Name,
                SeriesFolderFormat = profile.SeriesFolderFormat,
                UseSeasonFolders = profile.UseSeasonFolders,
                SeasonFolderFormat = profile.SeasonFolderFormat,
                SpecialsFolderFormat = profile.SpecialsFolderFormat,
                StandardEpisodeFormat = profile.StandardEpisodeFormat,
                DailyEpisodeFormat = profile.DailyEpisodeFormat,
                AnimeEpisodeFormat = profile.AnimeEpisodeFormat,
                MultiEpisodeStyle = profile.MultiEpisodeStyle,
                ReplaceIllegalCharacters = profile.ReplaceIllegalCharacters,
                ColonReplacement = profile.ColonReplacement,
                CustomColonReplacement = profile.CustomColonReplacement
            };

        public AnimeNamingProfile ToProfile() =>
            new(
                (Id ?? "").Trim().ToLowerInvariant(),
                (Name ?? "").Trim(),
                SeriesFolderFormat ?? "",
                SeasonFolderFormat ?? "",
                SpecialsFolderFormat ?? "",
                StandardEpisodeFormat ?? "",
                DailyEpisodeFormat ?? "",
                AnimeEpisodeFormat ?? "",
                UseSeasonFolders,
                MultiEpisodeStyle,
                ReplaceIllegalCharacters,
                ColonReplacement,
                CustomColonReplacement ?? "");
    }
}
