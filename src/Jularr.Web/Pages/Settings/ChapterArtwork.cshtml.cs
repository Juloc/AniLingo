using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.ChapterArtwork;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings;

/// <summary>Saves the chapter artwork section shown on Settings → AI.</summary>
public sealed class ChapterArtworkModel(
    CurrentAccountContext account,
    ChapterArtworkStore store,
    ChapterArtworkGlobalSettingsStore globalSettings,
    AppDbContext db) : PageModel
{
    public IActionResult OnGet() => Redirect("/Settings/Ai");

    public async Task<IActionResult> OnPostAsync(
        bool enabled,
        bool autoGenerate,
        string? style,
        string? quality,
        int variations,
        bool globalEnabled,
        string? storageRoot,
        CancellationToken cancellationToken)
    {
        var current = await store.GetPreferencesAsync(account.ProfileId, cancellationToken);

        // Generation choices write to shared media storage and stay owner-only.
        var preferences = account.IsOwner
            ? new ChapterArtworkPreferences(
                enabled,
                autoGenerate,
                ChapterArtworkNames.ParseStyle(style),
                ChapterArtworkNames.ParseQuality(quality),
                Math.Clamp(variations, 1, ChapterArtworkPreferences.MaxVariations))
            : current with { Enabled = enabled };

        await store.SavePreferencesAsync(account.ProfileId, preferences, cancellationToken);
        var ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TempData["Status"] = ui["settings.chapterArtwork.saved"];

        if (account.IsOwner)
        {
            try
            {
                await globalSettings.SaveAsync(
                    new ChapterArtworkGlobalSettings(globalEnabled, storageRoot),
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // The only rejected value is a storage folder that is not an absolute path.
                TempData["Status"] = ui["settings.chapterArtwork.storageRootNotAbsolute"];
            }
        }

        return Redirect("/Settings/Ai");
    }
}
