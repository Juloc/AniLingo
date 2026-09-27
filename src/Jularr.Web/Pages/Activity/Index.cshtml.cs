using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Reading;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Activity;

/// <summary>
/// The signed-in user's own history: playback (the Home history source), reading progress
/// (the Continue reading source) and their own acquisition requests. Every query is scoped to
/// the current profile, including for the owner.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext account,
    EpisodeProgressService progress,
    AcquisitionAccessStore requests) : PageModel
{
    public const int RequestLimit = 50;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<PlaybackHistoryItem> Playback { get; private set; } = [];
    public IReadOnlyList<ContinueReadingItem> Reading { get; private set; } = [];
    public IReadOnlyList<AcquisitionRequest> Requests { get; private set; } = [];

    public bool IsEmpty => Playback.Count == 0 && Reading.Count == 0 && Requests.Count == 0;

    /// <summary>Kind, chapter or page, and progress of one reading entry, in the Continue reading wording.</summary>
    public string ReadingSummary(ContinueReadingItem item)
    {
        var kind = item.Kind switch
        {
            ContinueReadingKind.Book => Ui["home.continueReading.kind.book"],
            ContinueReadingKind.Manga => Ui["home.continueReading.kind.manga"],
            _ => Ui["home.continueReading.kind.novel"]
        };
        var position = item.PageNumber is { } page && item.PageCount is { } pages
            ? Ui.Format("home.continueReading.chapterPage", ("chapter", item.ChapterLabel), ("page", page), ("pages", pages))
            : Ui.Format("home.continueReading.chapter", ("chapter", item.ChapterLabel));
        return $"{kind} · {position} · {Ui.Format("activity.progress", ("percent", item.ProgressPercent))}";
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Playback = await progress.GetHistoryAsync(cancellationToken);
        Reading = await new ContinueReadingQuery(db).GetAsync(account.ProfileId, cancellationToken: cancellationToken);
        Requests = await requests.ListAsync(
            kind: null,
            requestedByProfileId: account.ProfileId,
            openOnly: false,
            limit: RequestLimit,
            cancellationToken);
    }
}
