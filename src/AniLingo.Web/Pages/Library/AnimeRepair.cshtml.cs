using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

// Owner-only per-anime repair tools: rescan one anime's folder, refresh its local sidecar
// subtitles/NFO/artwork, force a media re-analysis, and identify/fix its AniList match without
// touching any other anime or requiring a full library scan (issue #133).
[Authorize(Roles = AccountRoles.Owner)]
public sealed class AnimeRepairModel(
    AppDbContext db,
    AnimeRepairService repair,
    AnimeMetadataService metadataService,
    LibraryScanCoordinator scans,
    CurrentAccountContext currentAccount,
    OperationRunner operations) : PageModel
{
    public const string MatchOperationKind = "anime-metadata-match";
    public const string RefreshMetadataOperationKind = "anime-metadata-refresh";
    public const string RefreshLocalOperationKind = "anime-repair-refresh-local";
    public const string ReanalyzeOperationKind = "anime-repair-reanalyze-media";
    private const string OperationCategory = "Anime";

    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public AnimeMetadata? Metadata { get; private set; }
    public AnimeRepairFolder? Folder { get; private set; }
    public IReadOnlyList<ActiveLibraryScan> ActiveScans { get; private set; } = [];
    public string SearchQuery { get; private set; } = "";
    public IReadOnlyList<AnimeRepairMatchCandidate> Candidates { get; private set; } = [];

    public string? Status => TempData["Status"] as string;
    public string? Error => TempData["Error"] as string;
    public Guid? OperationId =>
        TempData["RepairOperation"] is string value && Guid.TryParse(value, out var id) ? id : null;

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        string? q,
        CancellationToken cancellationToken)
    {
        var anime = await db.Anime
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (anime is null)
        {
            return NotFound();
        }

        AnimeId = anime.Id;
        Metadata = await metadataService.GetAsync(id, cancellationToken);
        AnimeTitle = Metadata?.PreferredTitle ?? anime.Title;
        Folder = await repair.ResolveFolderAsync(id, cancellationToken);
        SearchQuery = string.IsNullOrWhiteSpace(q) ? anime.Title : q.Trim();

        if (Folder is not null)
        {
            ActiveScans = scans.GetActive(Folder.RootId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            try
            {
                Candidates = await repair.SearchCandidatesAsync(id, SearchQuery, cancellationToken);
            }
            catch (MetadataProviderException exception)
            {
                TempData["Error"] = exception.Message;
            }
        }

        return Page();
    }

    // Queues a folder-scoped run through the one scan entry point (LibraryScanCoordinator), the
    // same way the filesystem watcher requests a folder scan; the coordinator's per-root guard
    // applies exactly as it does there.
    public async Task<IActionResult> OnPostRescanAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await repair.RescanFolderAsync(id, currentAccount.ProfileId, cancellationToken);
        TempData[result.Queued ? "Status" : "Error"] = result.Message;
        if (result.OperationId is Guid operationId)
        {
            TempData["RepairOperation"] = operationId.ToString("D");
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshLocalAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    RefreshLocalOperationKind,
                    OperationCategory,
                    "Refresh local subtitles/NFO/artwork",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                (_, token) => repair.RefreshLocalAsync(id, token),
                null,
                cancellationToken);

            TempData["Status"] =
                $"Refreshed {result.EpisodesConsidered} episode(s): {result.SubtitlesImported} subtitle(s) imported, " +
                $"{result.ArtworkImported} artwork file(s) updated, {result.ArtworkUnchanged} unchanged" +
                (result.NfoWarnings > 0 ? $", {result.NfoWarnings} NFO file(s) ignored." : ".");
        }
        catch (Exception exception)
        {
            TempData["Error"] = $"Local refresh failed: {exception.Message}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReanalyzeAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await operations.RunAsync(
                new OperationDescriptor(
                    ReanalyzeOperationKind,
                    OperationCategory,
                    "Re-analyse anime media",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                (_, token) => repair.ReanalyzeMediaAsync(id, token),
                null,
                cancellationToken);

            TempData["Status"] =
                $"Re-analysed {result.MediaFilesConsidered} media file(s): {result.Analyzed} succeeded, " +
                $"{result.Failed} failed, {result.Deferred} deferred.";
        }
        catch (Exception exception)
        {
            TempData["Error"] = $"Media re-analysis failed: {exception.Message}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMatchAsync(
        Guid id,
        string provider,
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    MatchOperationKind,
                    OperationCategory,
                    "Match anime metadata",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (_, token) =>
                {
                    var result = await metadataService.MatchAsync(id, provider, externalId, token);
                    if (!result.Success)
                    {
                        throw new InvalidOperationException(
                            result.Error ?? "Anime metadata could not be matched.");
                    }
                },
                "Anime metadata matched.",
                cancellationToken);

            TempData["Status"] = "Anime metadata matched.";
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or InvalidOperationException)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRefreshMetadataAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    RefreshMetadataOperationKind,
                    OperationCategory,
                    "Refresh anime metadata",
                    ProfileId: currentAccount.ProfileId,
                    Lane: OperationLane.Normal,
                    Retryable: false),
                async (_, token) =>
                {
                    if (!await metadataService.RefreshAsync(id, token))
                    {
                        throw new InvalidOperationException("Metadata could not be refreshed.");
                    }
                },
                "Anime metadata refreshed.",
                cancellationToken);

            TempData["Status"] = "Anime metadata refreshed.";
        }
        catch (Exception exception) when (
            exception is MetadataProviderException or InvalidOperationException)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToPage(new { id });
    }
}
