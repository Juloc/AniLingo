using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Watchlist;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    WatchlistStore watchlist,
    FranchiseStore franchises,
    FranchiseService franchiseService) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public IReadOnlyList<WatchlistItem> Items { get; private set; } = [];

    public IReadOnlyList<FranchiseSummary> Franchises { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Items = await watchlist.GetEffectiveAsync(account.ProfileId, cancellationToken);
        Franchises = await franchises.ListFollowedAsync(account.ProfileId, cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        string mediaType,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (!TryIdentity(mediaType, provider, externalId, out var identity))
        {
            return BadRequest();
        }

        await watchlist.UnfollowAsync(account.ProfileId, identity, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFollowFranchiseAsync(
        string mediaType,
        string provider,
        string externalId,
        string title,
        string? nativeTitle,
        string? coverImageUrl,
        string? format,
        string? status,
        int? year,
        Guid? localMediaId,
        string? detailsUrl,
        CancellationToken cancellationToken)
    {
        if (!TryDraft(
                mediaType,
                provider,
                externalId,
                title,
                nativeTitle,
                coverImageUrl,
                format,
                status,
                year,
                localMediaId,
                detailsUrl,
                out var draft))
        {
            return BadRequest();
        }

        await franchiseService.FollowFromSeedAsync(account.ProfileId, draft, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUnfollowFranchiseAsync(
        Guid franchiseId,
        CancellationToken cancellationToken)
    {
        await franchiseService.UnfollowAsync(account.ProfileId, franchiseId, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRefreshFranchiseAsync(
        Guid franchiseId,
        CancellationToken cancellationToken)
    {
        try
        {
            await franchiseService.RefreshAsync(franchiseId, cancellationToken);
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or NovelMetadataProviderException)
        {
            TempData["Status"] = "Franchise metadata could not be refreshed. The local follow remains active.";
        }

        return RedirectToPage();
    }

    internal static bool TryDraft(
        string? mediaType,
        string? provider,
        string? externalId,
        string? title,
        string? nativeTitle,
        string? coverImageUrl,
        string? format,
        string? status,
        int? year,
        Guid? localMediaId,
        string? detailsUrl,
        out WatchlistDraft draft)
    {
        draft = null!;
        if (!TryIdentity(mediaType, provider, externalId, out var identity) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Trim().Length > 500)
        {
            return false;
        }

        if (year is < 1800 or > 3000)
        {
            return false;
        }

        draft = new WatchlistDraft(
            identity,
            title.Trim(),
            Limit(nativeTitle, 500),
            SafeUrl(coverImageUrl),
            Limit(format, 80),
            Limit(status, 80),
            year,
            localMediaId,
            SafeUrl(detailsUrl));
        return true;
    }

    private static bool TryIdentity(
        string? mediaType,
        string? provider,
        string? externalId,
        out WatchlistIdentity identity)
    {
        identity = null!;
        var type = WatchlistMediaTypeNames.Parse(mediaType);
        var normalizedProvider = provider?.Trim().ToLowerInvariant();
        var normalizedId = externalId?.Trim();
        if (type is null ||
            string.IsNullOrWhiteSpace(normalizedProvider) ||
            normalizedProvider.Length > 80 ||
            string.IsNullOrWhiteSpace(normalizedId) ||
            normalizedId.Length > 200)
        {
            return false;
        }

        identity = new WatchlistIdentity(type.Value, normalizedProvider, normalizedId);
        return true;
    }

    private static string? Limit(string? value, int max)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null ? null : normalized[..Math.Min(max, normalized.Length)];
    }

    private static string? SafeUrl(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return null;
        }

        if (normalized.StartsWith('/') && !normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return normalized.Length <= 2048 ? normalized : null;
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               uri.Scheme is "https" or "http" &&
               normalized.Length <= 2048
            ? normalized
            : null;
    }
}
