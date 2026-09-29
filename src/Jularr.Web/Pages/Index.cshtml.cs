using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Reading;
using Jularr.Web.Features.Watchlist;
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
    public IReadOnlyList<HomeEpisode> RecentEpisodes { get; private set; } = [];
    public IReadOnlyList<ContinueWatchingItem> ContinueWatching { get; private set; } = [];

    /// <summary>Most slides the Home spotlight banner shows.</summary>
    public const int SpotlightLimit = 3;

    /// <summary>A watchlist release counts as "new" this many days after it came out.</summary>
    public const int SpotlightRecentDays = 14;

    /// <summary>Upcoming watchlist releases are considered this many days ahead.</summary>
    public const int SpotlightUpcomingDays = 30;

    /// <summary>
    /// The spotlight banner. It only ever shows the profile's own media: the most recent
    /// Continue Watching series, or — when nothing is in progress — the newest/nearest release
    /// of a followed watchlist work from the local release cache. Empty means no banner.
    /// </summary>
    public IReadOnlyList<HomeSpotlightSlide> Spotlight { get; private set; } = [];

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
        Spotlight = ContinueWatching.Count > 0
            ? await LoadWatchingSpotlightAsync(cancellationToken)
            : await LoadWatchlistSpotlightAsync(cancellationToken);

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

    /// <summary>"S01 · Episode 4 · 32 min left" — shared by the Continue Watching cards and the spotlight.</summary>
    public string WatchingCaption(ContinueWatchingItem item)
    {
        var episodeLabel = Ui.Format(
            "home.continueWatching.episode",
            ("season", item.SeasonNumber.ToString("00")),
            ("episode", item.EpisodeNumber));
        if (item.ResumePositionMs <= 0)
        {
            return episodeLabel;
        }

        var caption = item.RemainingMs is { } remainingMs
            ? Ui.Format(
                "home.continueWatching.remaining",
                ("minutes", Math.Max(1, (int)Math.Ceiling(remainingMs / 60000d))))
            : Ui["home.continueWatching.resume"];
        return $"{episodeLabel} · {caption}";
    }

    /// <summary>Up to three in-progress series, newest first, backed by their wide artwork when cached locally.</summary>
    private async Task<IReadOnlyList<HomeSpotlightSlide>> LoadWatchingSpotlightAsync(CancellationToken cancellationToken)
    {
        var items = ContinueWatching
            .OrderByDescending(item => item.UpdatedAt)
            .DistinctBy(item => item.AnimeId)
            .Take(SpotlightLimit)
            .ToArray();
        var animeIds = items.Select(item => item.AnimeId).ToArray();
        var banners = await db.AnimeMetadata.AsNoTracking()
            .Where(metadata => animeIds.Contains(metadata.AnimeId))
            .Select(metadata => new { metadata.AnimeId, metadata.BannerImageUrl })
            .ToDictionaryAsync(row => row.AnimeId, row => row.BannerImageUrl, cancellationToken);

        return items
            .Select(item =>
            {
                // A wide backdrop (cached fanart, else the provider banner) fills the banner; without
                // one the poster is shown as-is over a background derived from it.
                var backdrop = AnimeArtworkStore.ResolveFanartUrl(item.AnimeId, banners.GetValueOrDefault(item.AnimeId));
                return new HomeSpotlightSlide(
                    Ui["home.continueWatching.eyebrow"],
                    item.AnimeTitle,
                    WatchingCaption(item),
                    item.ResumePositionMs > 0 ? item.Percent : null,
                    string.IsNullOrWhiteSpace(backdrop) ? item.CoverImageUrl : backdrop,
                    !string.IsNullOrWhiteSpace(backdrop),
                    $"/Library/Episode/{item.EpisodeId}",
                    item.ResumePositionMs > 0 ? Ui["home.continueWatching.resume"] : Ui["home.spotlight.play"],
                    $"/Library/Anime/{item.AnimeId}",
                    Ui["home.spotlight.details"]);
            })
            .ToArray();
    }

    /// <summary>
    /// The newest release of each followed work (released in the last two weeks, newest first),
    /// then the nearest upcoming ones. Reads only the local release cache — no provider calls.
    /// </summary>
    private async Task<IReadOnlyList<HomeSpotlightSlide>> LoadWatchlistSpotlightAsync(CancellationToken cancellationToken)
    {
        ReleaseMediaType? mediaType = ActiveType switch
        {
            DiscoveryCategory.Anime => ReleaseMediaType.Anime,
            DiscoveryCategory.Manga => ReleaseMediaType.Manga,
            DiscoveryCategory.LightNovel => ReleaseMediaType.LightNovel,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(currentAccount.ProfileId) || ActiveType == DiscoveryCategory.Book)
        {
            return [];
        }

        var zone = CalendarTimeZone.Resolve(HttpContext?.Request.Cookies[CalendarTimeZone.CookieName]);
        var presenter = new ReleaseCalendarPresenter(Ui, zone, DateTimeOffset.UtcNow);
        var source = new WatchlistReleaseEventSource(
            new ReleaseCalendarCacheStore(db),
            new WatchlistStore(db),
            new WatchlistLibraryResolver(db));
        var events = await source.GetEventsAsync(
            new ReleaseEventQuery(
                presenter.Today.AddDays(-SpotlightRecentDays),
                presenter.Today.AddDays(SpotlightUpcomingDays),
                zone,
                presenter.Now,
                MediaType: mediaType,
                ProfileId: currentAccount.ProfileId),
            cancellationToken);

        return events
            .Select(release => (Release: release, Day: release.Date.Period(zone)?.Start))
            .Where(row => row.Day is not null)
            .OrderBy(row => row.Day <= presenter.Today ? 0 : 1)
            .ThenBy(row => row.Day <= presenter.Today
                ? presenter.Today.DayNumber - row.Day!.Value.DayNumber
                : row.Day!.Value.DayNumber - presenter.Today.DayNumber)
            .DistinctBy(row => row.Release.MediaId)
            .Take(SpotlightLimit)
            .Select(row => new HomeSpotlightSlide(
                Ui["home.spotlight.watchlist"],
                row.Release.Title,
                string.Join(
                    " · ",
                    new[] { presenter.UnitLabel(row.Release), presenter.DateLabel(row.Release.Date) }
                        .Where(text => !string.IsNullOrEmpty(text))),
                null,
                row.Release.CoverImageUrl,
                false,
                ReleaseCalendarPresenter.Href(row.Release) ?? "/Watchlist",
                Ui["home.spotlight.details"],
                "/Calendar",
                Ui["nav.calendar"]))
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

    /// <summary>One spotlight banner slide; every text is already localized.</summary>
    public sealed record HomeSpotlightSlide(
        string Label,
        string Title,
        string Subtitle,
        int? ProgressPercent,
        string? ImageUrl,
        bool ImageIsBackdrop,
        string PrimaryHref,
        string PrimaryLabel,
        string SecondaryHref,
        string SecondaryLabel);

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
