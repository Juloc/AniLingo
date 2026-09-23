using AniLingo.Web.Features.Sonarr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class SonarrModel(
    SonarrConnectionStore connectionStore,
    SonarrArtworkImportService sonarrService,
    ILogger<SonarrModel> logger) : PageModel
{

    [BindProperty]
    public string BaseUrl { get; set; } = "http://sonarr:8989";

    [BindProperty]
    public string? ApiKey { get; set; }

    public bool HasSavedApiKey { get; private set; }
    public string? Notice => TempData["SonarrNotice"] as string;
    public string? Error => TempData["SonarrError"] as string;
    public SonarrArtworkImportResult? LastImport =>
        TempData.TryGetValue("SonarrImportResult", out var raw) && raw is string json
            ? System.Text.Json.JsonSerializer.Deserialize<SonarrArtworkImportResult>(json)
            : null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var saved = await connectionStore.LoadAsync(cancellationToken);
        if (saved is not null)
        {
            BaseUrl = saved.BaseUrl;
            HasSavedApiKey = true;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var settings = await ResolveSubmittedSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return RedirectToPage();
        }

        await connectionStore.SaveAsync(settings, cancellationToken);
        TempData["SonarrNotice"] = "Sonarr connection saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        var settings = await ResolveSubmittedSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return RedirectToPage();
        }

        var result = await sonarrService.TestAsync(settings, cancellationToken);

        if (result.Success)
        {
            await connectionStore.SaveAsync(settings, cancellationToken);
            TempData["SonarrNotice"] = result.Message;
        }
        else
        {
            TempData["SonarrError"] = result.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
    {
        var settings = await ResolveSubmittedSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return RedirectToPage();
        }

        try
        {
            var test = await sonarrService.TestAsync(settings, cancellationToken);
            if (!test.Success)
            {
                TempData["SonarrError"] = test.Message;
                return RedirectToPage();
            }

            await connectionStore.SaveAsync(settings, cancellationToken);
            var result = await sonarrService.ImportAsync(settings, cancellationToken);

            TempData["SonarrNotice"] =
                $"Imported {result.PosterCount} posters and {result.FanartCount} fanart images.";
            TempData["SonarrImportResult"] =
                System.Text.Json.JsonSerializer.Serialize(result);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Sonarr artwork import failed.");
            TempData["SonarrError"] = "Sonarr artwork import failed.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["SonarrError"] = exception.Message;
        }

        return RedirectToPage();
    }

    private async Task<SonarrConnectionSettings?> ResolveSubmittedSettingsAsync(
        CancellationToken cancellationToken)
    {
        var baseUrl = SonarrConnectionStore.NormalizeBaseUrl(BaseUrl);
        var apiKey = ApiKey?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var saved = await connectionStore.LoadAsync(cancellationToken);
            apiKey = saved?.ApiKey;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            TempData["SonarrError"] = "Sonarr URL is required.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            TempData["SonarrError"] = "Sonarr API key is required.";
            return null;
        }

        return new SonarrConnectionSettings(baseUrl, apiKey);
    }
}
