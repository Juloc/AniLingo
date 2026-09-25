using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.MediaMapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class MappingSegmentsModel(
    ReadingSegmentMappingStore segmentMappings,
    CurrentAccountContext account) : PageModel
{
    public IReadOnlyList<ReadingMediaSegmentMapping> Mappings { get; private set; } = [];

    [BindProperty]
    [Required]
    public string MediaType { get; set; } = "manga";

    [BindProperty]
    [Required]
    public string LocalId { get; set; } = "";

    [BindProperty]
    public double LocalChapterStart { get; set; } = 1;

    [BindProperty]
    public double LocalChapterEnd { get; set; } = 1;

    [BindProperty]
    public int RemoteChapterStart { get; set; } = 1;

    [BindProperty]
    [Required]
    public string ExternalId { get; set; } = "";

    [BindProperty]
    public string? PreferredTitle { get; set; }

    [BindProperty]
    public int? RemoteChapterCount { get; set; }

    [BindProperty]
    public int? LocalVolumeStart { get; set; }

    [BindProperty]
    public int? LocalVolumeEnd { get; set; }

    [BindProperty]
    public int? RemoteVolumeStart { get; set; }

    public async Task<IActionResult> OnGetAsync(
        string? mediaType,
        string? localId,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            MediaType = mediaType.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(localId))
        {
            LocalId = localId.Trim();
        }

        Mappings = await segmentMappings.ListAsync(
            cancellationToken: cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        if (!Guid.TryParse(LocalId, out var localId))
        {
            ModelState.AddModelError(
                nameof(LocalId),
                "Local media ID must be a valid GUID.");
        }

        if (!int.TryParse(ExternalId, out var remoteId) || remoteId <= 0)
        {
            ModelState.AddModelError(
                nameof(ExternalId),
                "AniList media ID must be a positive integer.");
        }

        if (!ModelState.IsValid)
        {
            Mappings = await segmentMappings.ListAsync(
                cancellationToken: cancellationToken);
            return Page();
        }

        try
        {
            await segmentMappings.AddAsync(
                new ReadingMediaSegmentMapping(
                    Guid.NewGuid(),
                    MediaType,
                    localId.ToString(),
                    LocalChapterStart,
                    LocalChapterEnd,
                    RemoteChapterStart,
                    "anilist",
                    remoteId.ToString(),
                    PreferredTitle,
                    RemoteChapterCount,
                    LocalVolumeStart,
                    LocalVolumeEnd,
                    RemoteVolumeStart,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            TempData["Status"] = "Reading segment mapping added.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["Status"] = exception.Message;
        }

        return RedirectToPage(
            new
            {
                mediaType = MediaType,
                localId = localId.ToString()
            });
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!account.IsOwner)
        {
            return Forbid();
        }

        var removed = await segmentMappings.RemoveAsync(
            id,
            cancellationToken);
        TempData["Status"] = removed
            ? "Reading segment mapping removed."
            : "Reading segment mapping was already gone.";

        return RedirectToPage();
    }

    public static string? TargetUrl(ReadingMediaSegmentMapping mapping)
    {
        if (!Guid.TryParse(mapping.LocalId, out var id))
        {
            return null;
        }

        return mapping.MediaType.ToLowerInvariant() switch
        {
            "manga" => $"/Manga/Series/{id}",
            "novel" => $"/Novels/Work/{id}",
            _ => null
        };
    }
}
