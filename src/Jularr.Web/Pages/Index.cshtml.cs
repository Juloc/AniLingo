using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Reading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages;

public sealed class IndexModel(AppDbContext db, CurrentAccountContext currentAccount) : PageModel
{
    private readonly EpisodeProgressService progress = new(db, currentAccount);

    /// <summary>
    /// The Home media-type filter chips, in display order. Each maps to the
    /// <c>type</c> query parameter (parsed the same way as Discover's
    /// <c>category</c>) and to the Discover category it links to.
    /// </summary>
    public static readonly IReadOnlyList<HomeTypeChip> TypeChips =
    [
        new(DiscoveryCategory.All, "all", "home.filter.all"),
        new(DiscoveryCategory.Anime, "anime", "home.filter.anime"),
        new(DiscoveryCategory.Manga, "manga", "home.filter.manga"),
        new(DiscoveryCategory.LightNovel, "novels", "home.filter.novels"),
        new(DiscoveryCategory.Book, "books", "home.filter.books")
    ];

    public int DueReviews { get; private set; }
    public int AnimeCount { get; private set; }
    public int EpisodeCount { get; private set; }
    public IReadOnlyList<HomeEpisode> RecentEpisodes { get; private set; } = [];
    public IReadOnlyList<ContinueWatchingItem> ContinueWatching { get; private set; } = [];

    /// <summary>The active Home media-type filter chip, from the <c>type</c> query parameter.</summary>
    public DiscoveryCategory ActiveType { get; private set; } = DiscoveryCategory.All;

    /// <summary>Discover link for the Continue Watching heading; anime is the only Continue Watching medium.</summary>
    public string ContinueWatchingDiscoverUrl => "/Discover?category=anime&mode=my-list";

    /// <summary>
    /// Discover link for the Continue Reading heading. Matches the active
    /// filter chip so a filtered row always points at the same category in
    /// Discover; unset (all media) when no specific chip is active.
    /// </summary>
    public string ContinueReadingDiscoverUrl => ActiveType switch
    {
        DiscoveryCategory.Manga => "/Discover?category=manga&mode=my-list",
        DiscoveryCategory.LightNovel => "/Discover?category=light-novel&mode=my-list",
        DiscoveryCategory.Book => "/Discover?category=book&mode=my-list",
        _ => "/Discover?mode=my-list"
    };

    /// <summary>
    /// Most recently read unfinished Novels, Books and Manga of the current
    /// profile, newest first, each with its exact reader resume URL.
    /// </summary>
    public IReadOnlyList<ContinueReadingItem> ContinueReading { get; private set; } = [];
    public IReadOnlyList<PlaybackHistoryItem> PlaybackHistory { get; private set; } = [];
    public int PlaybackHistoryLimit => EpisodeProgressService.HistoryLimit;
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    /// <summary>
    /// Due-review widget: resolved HomeWidget and Reviews capabilities at profile
    /// scope. HomeWidget is off by default in every mode, including Study; the
    /// user opts in through Learning settings.
    /// </summary>
    public bool ShowLearningHomeWidget { get; private set; }

    /// <summary>
    /// Resolved ContentMetrics capability for the Anime media type. Controls
    /// preparation percentages on recently discovered episode cards.
    /// </summary>
    public bool ShowContentMetrics { get; private set; }

    public async Task<IActionResult> OnPostClearHistoryAsync(CancellationToken cancellationToken)
    {
        await progress.ClearHistoryAsync(cancellationToken);

        var ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);
        TempData["Status"] = ui["home.history.cleared"];
        return RedirectToPage();
    }

    public async Task OnGetAsync(CancellationToken cancellationToken, string? type = null)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        ActiveType = DiscoveryRequest.ParseCategory(type);

        ContinueWatching = ActiveType is DiscoveryCategory.All or DiscoveryCategory.Anime
            ? await progress.GetContinueWatchingAsync(cancellationToken: cancellationToken)
            : [];
        PlaybackHistory = await progress.GetHistoryAsync(cancellationToken);

        var continueReading = await new ContinueReadingQuery(db).GetAsync(
            currentAccount.ProfileId,
            cancellationToken: cancellationToken);
        ContinueReading = FilterContinueReading(continueReading, ActiveType);

        var configuration = new LearningConfigurationStore(db);
        var profileLearning = await configuration.ResolveProfileAsync(
            currentAccount.ProfileId,
            cancellationToken);
        var animeLearning = await configuration.ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(LearningMediaType.Anime),
            cancellationToken);

        ShowLearningHomeWidget =
            profileLearning.IsEnabled(LearningCapability.HomeWidget)
            && profileLearning.IsEnabled(LearningCapability.Reviews);
        ShowContentMetrics =
            animeLearning.IsEnabled(LearningCapability.ContentMetrics);

        var now = DateTime.UtcNow;

        if (ShowLearningHomeWidget)
        {
            DueReviews = await LearningQueries
                .DueCards(db, currentAccount.ProfileId, now)
                .CountAsync(cancellationToken);
        }

        AnimeCount = await db.Anime.AsNoTracking().CountAsync(cancellationToken);
        EpisodeCount = await db.Episodes.AsNoTracking().CountAsync(cancellationToken);

        var recentEpisodes = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            join metadataValue in db.AnimeMetadata.AsNoTracking()
                on anime.Id equals metadataValue.AnimeId into metadataRows
            from metadata in metadataRows.DefaultIfEmpty()
            orderby episode.DiscoveredAt descending
            select new HomeEpisode(
                episode.Id,
                anime.Id,
                metadata == null ? anime.Title : metadata.PreferredTitle,
                episode.SeasonNumber,
                episode.Number,
                0,
                0,
                metadata == null ? null : metadata.CoverImageUrl))
            .Take(10)
            .ToListAsync(cancellationToken);

        // Vocabulary coverage is only computed when the resolved Anime scope
        // shows content metrics; otherwise Home never touches learning tables.
        var coverage = ShowContentMetrics
            ? await LoadCoverageAsync(
                recentEpisodes.Select(x => x.Id).ToArray(),
                cancellationToken)
            : new Dictionary<Guid, (int Total, int Prepared)>();

        RecentEpisodes = recentEpisodes
            .Select(row =>
            {
                coverage.TryGetValue(row.Id, out var totals);
                return row with
                {
                    TotalOccurrences = totals.Total,
                    PreparedOccurrences = totals.Prepared,
                    CoverImageUrl = AnimeArtworkStore.ResolvePosterUrl(
                        row.AnimeId,
                        row.CoverImageUrl)
                };
            })
            .ToArray();
    }

    /// <summary>Keeps only the reading items matching the active Home filter chip; "All" and "Anime" keep everything (Anime has no reading row of its own).</summary>
    private static IReadOnlyList<ContinueReadingItem> FilterContinueReading(
        IReadOnlyList<ContinueReadingItem> items,
        DiscoveryCategory activeType) =>
        activeType switch
        {
            DiscoveryCategory.Manga => items.Where(x => x.Kind == ContinueReadingKind.Manga).ToArray(),
            DiscoveryCategory.LightNovel => items.Where(x => x.Kind == ContinueReadingKind.Novel).ToArray(),
            DiscoveryCategory.Book => items.Where(x => x.Kind == ContinueReadingKind.Book).ToArray(),
            DiscoveryCategory.Anime => [],
            _ => items
        };

    private async Task<Dictionary<Guid, (int Total, int Prepared)>> LoadCoverageAsync(
        Guid[] episodeIds,
        CancellationToken cancellationToken)
    {
        if (episodeIds.Length == 0)
        {
            return [];
        }

        var totals = await db.EpisodeTerms
            .AsNoTracking()
            .Where(x => episodeIds.Contains(x.EpisodeId))
            .GroupBy(x => x.EpisodeId)
            .Select(group => new
            {
                EpisodeId = group.Key,
                Total = group.Sum(x => x.Occurrences)
            })
            .ToDictionaryAsync(x => x.EpisodeId, x => x.Total, cancellationToken);

        var prepared = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join state in LearningQueries.TermStates(db, currentAccount.ProfileId)
                    .Where(x =>
                        x.State == UserTermState.Known
                        || x.State == UserTermState.Learning)
                on episodeTerm.TermId equals state.TermId
            where episodeIds.Contains(episodeTerm.EpisodeId)
            group episodeTerm by episodeTerm.EpisodeId
            into episodeGroup
            select new
            {
                EpisodeId = episodeGroup.Key,
                Prepared = episodeGroup.Sum(x => x.Occurrences)
            })
            .ToDictionaryAsync(x => x.EpisodeId, x => x.Prepared, cancellationToken);

        return totals.ToDictionary(
            x => x.Key,
            x => (x.Value, prepared.GetValueOrDefault(x.Key)));
    }

    /// <summary>One Home media-type filter chip: its Discover category, the <c>?type=</c> query value, and its label key.</summary>
    public sealed record HomeTypeChip(DiscoveryCategory Category, string QueryValue, string LabelKey);

    /// <summary>Home's own link for a filter chip; the default ("all") chip keeps the plain root URL.</summary>
    public static string ChipHref(HomeTypeChip chip) =>
        chip.QueryValue == "all" ? "/" : $"/?type={chip.QueryValue}";

    public sealed record HomeEpisode(
        Guid Id,
        Guid AnimeId,
        string AnimeTitle,
        int SeasonNumber,
        int Number,
        int TotalOccurrences,
        int PreparedOccurrences,
        string? CoverImageUrl)
    {
        public int PreparationPercent => TotalOccurrences == 0
            ? 0
            : (int)Math.Floor((double)PreparedOccurrences / TotalOccurrences * 100);
    }
}
