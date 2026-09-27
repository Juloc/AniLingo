using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Features.Watchlist;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Discover;

public sealed class IndexModel(
    AniListMetadataProvider animeProvider,
    NovelAniListProvider readingProvider,
    BookCatalogService books,
    AniListAccountService aniListAccount,
    AppDbContext db,
    NovelImportService novels,
    NovelMetadataService novelMetadata,
    CurrentAccountContext account,
    OperationRunner operations,
    AcquisitionRequestService requests,
    AcquisitionAccessStore requestStore,
    WatchlistStore watchlist,
    FranchiseService franchiseService,
    ILogger<DiscoveryCoordinator> discoveryLogger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public bool IsOwner => account.IsOwner;

    /// <summary>The card action per AniList category: "add", "request" or "" (none).</summary>
    public IReadOnlyDictionary<string, string> AddActions { get; private set; } = new Dictionary<string, string>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var actions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (category, kind) in Categories)
        {
            actions[category] = AddAction(kind, await requests.GetCapabilitiesAsync(kind, cancellationToken));
        }

        AddActions = actions;
    }

    // Anime is added like in Sonarr. Manga and light novels have no automatic acquisition, so
    // other profiles request them; the owner keeps the import flows of the card.
    public static string AddAction(MediaAcquisitionKind kind, AcquisitionCapabilities access) =>
        !access.CanAdd ? ""
        : kind == MediaAcquisitionKind.Anime ? (access.AddCreatesRequest ? "request" : "add")
        : access.IsOwner ? ""
        : "request";

    public async Task<IActionResult> OnGetResultsAsync(
        string? q,
        string? category,
        string? mode,
        string? source,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var request = DiscoveryRequest.Parse(q, category, mode);
        var coordinator = new DiscoveryCoordinator(
            animeProvider,
            readingProvider,
            books,
            aniListAccount,
            db,
            discoveryLogger);

        var normalizedSource = source?.Trim().ToLowerInvariant();
        var includeAniList = normalizedSource is not "books";
        var includeBooks = normalizedSource is not "anilist";

        var result = await coordinator.GetAsync(
            request,
            account.ProfileId,
            account.IsOwner,
            includeAniList,
            includeBooks,
            cancellationToken);

        var open = (await requestStore.ListAsync(null, null, openOnly: true, limit: 500, cancellationToken))
            .Where(item => item.Provider == AniListMetadataProvider.ProviderKey)
            .GroupBy(item => (item.Kind, item.ExternalId))
            .ToDictionary(group => group.Key, group => AcquisitionAccessNames.Status(group.First().Status));

        var followed = await watchlist.GetEffectiveKeysAsync(account.ProfileId, cancellationToken);

        return new JsonResult(result with
        {
            Items = result.Items
                .Select(item =>
                {
                    var mapped = !item.IsLocal &&
                                 Categories.TryGetValue(item.Category, out var kind) &&
                                 open.TryGetValue((kind, item.ExternalId), out var status)
                        ? item with { RequestStatus = status }
                        : item;

                    return TryWatchIdentity(item.Category, item.Provider, item.ExternalId, out var identity)
                        ? mapped with { IsFollowed = followed.Contains(identity.Key) }
                        : mapped;
                })
                .ToArray()
        });
    }

    public async Task<IActionResult> OnPostWatchlistAsync(
        string? category,
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
        bool follow,
        CancellationToken cancellationToken)
    {
        if (!WatchlistDraftInput.TryCreate(
                category,
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

        if (follow)
        {
            await watchlist.FollowAsync(account.ProfileId, draft, cancellationToken);
        }
        else
        {
            await watchlist.UnfollowAsync(account.ProfileId, draft.Identity, cancellationToken);
        }

        return new JsonResult(new { followed = follow });
    }

    public async Task<IActionResult> OnPostFollowFranchiseAsync(
        string? category,
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
        CancellationToken cancellationToken)
    {
        if (!WatchlistDraftInput.TryCreate(
                category,
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
                out var draft) ||
            draft.Identity.MediaType != WatchlistMediaType.Anime ||
            !draft.Identity.ProviderKey.Equals(AniListMetadataProvider.ProviderKey, StringComparison.Ordinal))
        {
            return BadRequest();
        }

        var franchiseId = await franchiseService.FollowFromSeedAsync(
            account.ProfileId,
            draft,
            cancellationToken);
        return new JsonResult(new { followed = true, franchiseId });
    }

    private static bool TryWatchIdentity(
        string category,
        string provider,
        string externalId,
        out WatchlistIdentity identity) =>
        WatchlistDraftInput.TryIdentity(category, provider, externalId, out identity);

    /// <summary>Adds (anime, automatic) or requests an AniList title according to its access policy.</summary>
    public async Task<IActionResult> OnPostAddAsync(
        string? category,
        string? externalId,
        string? title,
        string? subtitle,
        string? coverImageUrl,
        CancellationToken cancellationToken)
    {
        if (category is null ||
            !Categories.TryGetValue(category, out var kind) ||
            !int.TryParse(externalId, out var id) ||
            id <= 0 ||
            string.IsNullOrWhiteSpace(title))
        {
            return BadRequest();
        }

        try
        {
            var request = await requests.SubmitAsync(
                new AcquisitionRequestDraft(
                    kind,
                    AniListMetadataProvider.ProviderKey,
                    id.ToString(),
                    title.Trim(),
                    string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim(),
                    string.IsNullOrWhiteSpace(coverImageUrl) ? null : coverImageUrl.Trim()),
                cancellationToken);
            return new JsonResult(new
            {
                status = AcquisitionAccessNames.Status(request.Status),
                message = request.StatusMessage,
                resultUrl = request.ResultUrl
            });
        }
        catch (AcquisitionAccessDeniedException)
        {
            return Forbid();
        }
    }

    private static readonly IReadOnlyDictionary<string, MediaAcquisitionKind> Categories =
        new Dictionary<string, MediaAcquisitionKind>(StringComparer.Ordinal)
        {
            ["anime"] = MediaAcquisitionKind.Anime,
            ["manga"] = MediaAcquisitionKind.Manga,
            ["light-novel"] = MediaAcquisitionKind.LightNovel
        };

    public async Task<IActionResult> OnPostImportSourceAsync(
        string sourceUrl,
        string? metadataProvider,
        string? metadataExternalId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    "discover-novel-import",
                    "Novels",
                    "Import discovered novel",
                    string.IsNullOrWhiteSpace(metadataExternalId)
                        ? null
                        : $"AniList {metadataExternalId.Trim()}",
                    account.ProfileId,
                    OperationLane.Normal,
                    IsDownload: true,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Importing novel source.",
                        cancellationToken: token);

                    var workId = await novels.ImportWorkAsync(
                        sourceUrl,
                        token);

                    if (!string.IsNullOrWhiteSpace(metadataProvider) &&
                        !string.IsNullOrWhiteSpace(metadataExternalId))
                    {
                        try
                        {
                            await operation.ReportAsync(
                                80,
                                "Matching imported novel metadata.",
                                cancellationToken: token);

                            await novelMetadata.MatchAsync(
                                workId,
                                metadataProvider,
                                metadataExternalId,
                                token);

                            return (WorkId: workId, Status: Ui["discover.import.novelMatched"]);
                        }
                        catch (Exception exception) when (
                            exception is InvalidOperationException or
                            NovelMetadataProviderException)
                        {
                            await operation.LogAsync(
                                OperationLogLevel.Warning,
                                "NovelMetadata",
                                "Novel import completed, but metadata matching needs attention.",
                                CancellationToken.None);

                            return (
                                WorkId: workId,
                                Status: Ui.Format(
                                    "discover.import.novelMatchAttention",
                                    ("reason", exception.Message)));
                        }
                    }

                    return (
                        WorkId: workId,
                        Status: Ui["discover.import.novelImported"]);
                },
                "Discovered novel imported.",
                cancellationToken);

            TempData["Status"] = result.Status;
            return RedirectToPage("/Novels/Work", new { id = result.WorkId });
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }
    }
}
