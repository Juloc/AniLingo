using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Tracking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// The calendar end to end on a migrated SQLite database with a fake AniList client: bounded
/// refresh into the cache, library-state merge, stale data when the provider fails, and the
/// read model's day grouping. No provider is called.
/// </summary>
[TestClass]
public sealed class ReleaseCalendarIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task RefreshFillsTheCacheAndTheCalendarMergesLibraryState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var anime = await fixture.AddAnimeAsync();
        var novel = await fixture.AddNovelAsync();
        var book = await fixture.AddBookAsync("2026-10-05");
        await fixture.AddBookAsync("an unknown year");
        await fixture.Monitoring.UpdateAsync(state =>
        {
            state.Anime["frieren"] = new AnimeMonitorSettings("frieren", true, false, new(), new());
            return state;
        });

        fixture.Client.Responses.Enqueue(new AniListReleaseSchedule(
            [
                new AniListReleaseMedia(154587, "ANIME", "RELEASING", 2026, 9, 21, null),
                new AniListReleaseMedia(999, "NOVEL", "NOT_YET_RELEASED", 2026, 11, null, null)
            ],
            [
                new AniListAiring(154587, 1, new DateTimeOffset(2026, 9, 21, 15, 0, 0, TimeSpan.Zero)),
                new AniListAiring(154587, 2, new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero))
            ],
            HasMoreAirings: true));
        fixture.Client.Responses.Enqueue(new AniListReleaseSchedule(
            [],
            [new AniListAiring(154587, 3, new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero))],
            HasMoreAirings: false));

        var result = await fixture.Refresher().RefreshDueAsync(CancellationToken.None);

        Assert.AreEqual(2, result.Refreshed);
        Assert.AreEqual(2, result.Requests, "One batch, two schedule pages.");
        CollectionAssert.AreEquivalent(new[] { 154587, 999 }, fixture.Client.RequestedIds.Single().ToArray());

        var again = await fixture.Refresher().RefreshDueAsync(CancellationToken.None);
        Assert.AreEqual(0, again.Requests, "Fresh data is not fetched again.");

        var calendar = await fixture.Service().GetAsync(
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 11, 14),
            TimeZoneInfo.Utc,
            ReleaseCalendarFilter.All,
            Now,
            includeUndated: true,
            CancellationToken.None);

        Assert.AreEqual(0, calendar.FailedSources.Count);
        var byDay = calendar.Days.Where(day => day.Events.Count > 0).ToDictionary(day => day.Date, day => day.Events);
        var premiere = byDay[new DateOnly(2026, 9, 21)].Single();
        Assert.AreEqual(ReleaseKind.SeasonPremiere, premiere.Kind);
        Assert.AreEqual(anime.Id, premiere.MediaId);
        Assert.AreEqual(fixture.EpisodeWithFile, premiere.UnitId);
        Assert.AreEqual(ReleaseLocalState.Available, premiere.Local.State);
        Assert.AreEqual("Frieren: Beyond Journey's End", premiere.Title, "The title comes from the canonical match.");

        Assert.AreEqual(ReleaseLocalState.Missing, byDay[new DateOnly(2026, 9, 28)].Single().Local.State);

        var upcomingDay = byDay[new DateOnly(2026, 10, 5)];
        Assert.AreEqual(ReleaseLocalState.Monitored, upcomingDay.Single(release => release.MediaType == ReleaseMediaType.Anime).Local.State);
        var publication = upcomingDay.Single(release => release.MediaType == ReleaseMediaType.Book);
        Assert.AreEqual(book, publication.MediaId);
        Assert.AreEqual(ReleaseDatePrecision.Day, publication.Date.Precision);
        Assert.AreEqual(ReleaseLocalState.Available, publication.Local.State);

        var seriesStart = calendar.Imprecise.Single();
        Assert.AreEqual(ReleaseMediaType.LightNovel, seriesStart.MediaType);
        Assert.AreEqual(novel, seriesStart.MediaId);
        Assert.AreEqual(ReleaseDate.FromMonth(2026, 11), seriesStart.Date);

        var upcoming = await fixture.Service().GetUpcomingAsync(ReleaseMediaType.Anime, anime.Id, TimeZoneInfo.Utc, Now, 3, CancellationToken.None);
        Assert.AreEqual(3, upcoming.Single().Unit?.Number, "Only the unreleased episode is upcoming.");

        var missing = await fixture.Service().GetAsync(
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 10, 14),
            TimeZoneInfo.Utc,
            new ReleaseCalendarFilter(new HashSet<ReleaseMediaType>(), ReleaseStateFilter.Missing),
            Now,
            includeUndated: false,
            CancellationToken.None);
        Assert.AreEqual(2, missing.Days.Single(day => day.Events.Count > 0).Events.Single().Unit?.Number);
    }

    [TestMethod]
    public async Task ProviderFailuresKeepCachedDataAndBackOff()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAnimeAsync();
        fixture.Client.Responses.Enqueue(new AniListReleaseSchedule(
            [new AniListReleaseMedia(154587, "ANIME", "RELEASING", 2026, 9, 21, null)],
            [new AniListAiring(154587, 3, new DateTimeOffset(2026, 10, 5, 15, 0, 0, TimeSpan.Zero))],
            HasMoreAirings: false));
        await fixture.Refresher().RefreshDueAsync(CancellationToken.None);

        fixture.Clock.Advance(TimeSpan.FromHours(13));
        fixture.Client.Failure = new MetadataProviderException("AniList metadata is currently unavailable.");
        var failed = await fixture.Refresher().RefreshDueAsync(CancellationToken.None);
        Assert.AreEqual(1, failed.Failed);

        var sources = await new ReleaseCalendarCacheStore(fixture.Db).GetSourcesAsync("anilist", CancellationToken.None);
        Assert.IsNotNull(sources["154587"].LastError);

        var retry = await fixture.Refresher().RefreshDueAsync(CancellationToken.None);
        Assert.AreEqual(0, retry.Requests, "A failed source is retried only after the back-off.");

        var calendar = await fixture.Service().GetAsync(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            TimeZoneInfo.Utc,
            ReleaseCalendarFilter.All,
            fixture.Clock.GetUtcNow(),
            includeUndated: false,
            CancellationToken.None);
        Assert.AreEqual(1, calendar.Days.Sum(day => day.Events.Count), "Stale-but-usable data stays visible.");

        fixture.Client.Failure = new ReleaseProviderRateLimitedException(TimeSpan.FromMinutes(3));
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        var limited = await fixture.Refresher().RefreshDueAsync(CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromMinutes(3), limited.RetryAfter);
    }

    [TestMethod]
    public async Task RefreshReplacesUpcomingReleasesAndKeepsHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new ReleaseCalendarCacheStore(fixture.Db);
        var windowStart = Now.AddDays(-35);
        var old = ReleaseDate.FromInstant(Now.AddDays(-60));
        var moved = ReleaseDate.FromInstant(Now.AddDays(3));

        await store.SaveAsync(
            "anilist",
            [new ReleaseSourceSnapshot("1", "RELEASING",
            [
                new CachedRelease("anilist", "1", ReleaseKind.Episode, 2, old),
                new CachedRelease("anilist", "1", ReleaseKind.Episode, 9, ReleaseDate.FromInstant(Now.AddDays(2))),
                new CachedRelease("anilist", "1", ReleaseKind.SeasonPremiere, 1, ReleaseDate.FromMonth(2027, 1))
            ])],
            Now.AddDays(-90),
            Now.UtcDateTime,
            CancellationToken.None);

        await store.SaveAsync(
            "anilist",
            [new ReleaseSourceSnapshot("1", "RELEASING", [new CachedRelease("anilist", "1", ReleaseKind.Episode, 10, moved)])],
            windowStart,
            Now.UtcDateTime,
            CancellationToken.None);

        var cached = await store.GetReleasesAsync("anilist", new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31), true, null, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { 2, 10 }, cached.Select(release => release.UnitNumber).ToArray(),
            "History before the window stays; the old future episode and the undated premiere are replaced.");
        Assert.AreEqual(moved, cached.Single(release => release.UnitNumber == 10).Date);
    }

    private sealed class FakeClient : IAniListReleaseScheduleClient
    {
        public Queue<AniListReleaseSchedule> Responses { get; } = new();

        public List<IReadOnlyList<int>> RequestedIds { get; } = [];

        public Exception? Failure { get; set; }

        public Task<AniListReleaseSchedule> FetchAsync(IReadOnlyList<int> ids, DateTimeOffset from, DateTimeOffset to, int page, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (page == 1)
            {
                RequestedIds.Add(ids);
            }

            return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : new AniListReleaseSchedule([], [], false));
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
            Mappings = new AniListAccountStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AniListAccountStore>.Instance,
                new DirectoryInfo(Path.Combine(directory, "integrations")));
            Monitoring = new AnimeMonitoringStore(directory);
        }

        public AppDbContext Db { get; }

        public AniListAccountStore Mappings { get; }

        public AnimeMonitoringStore Monitoring { get; }

        public FakeClient Client { get; } = new();

        public MutableClock Clock { get; } = new(Now);

        public Guid EpisodeWithFile { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"jularr-calendar-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(directory, db);
        }

        public ReleaseCalendarRefresher Refresher() =>
            new(Db, new ReleaseCalendarCacheStore(Db), Mappings, Client, NullLogger<ReleaseCalendarRefresher>.Instance, Clock)
            {
                RequestSpacing = TimeSpan.Zero
            };

        public ReleaseCalendarService Service() =>
            new(
                [
                    new AniListReleaseEventSource(Db, new ReleaseCalendarCacheStore(Db), Mappings, Monitoring),
                    new NovelChapterReleaseEventSource(Db),
                    new BookReleaseEventSource(Db)
                ],
                NullLogger<ReleaseCalendarService>.Instance);

        public async Task<Anime> AddAnimeAsync()
        {
            var root = new LibraryRoot { Name = "Anime", Path = Path.Combine(directory, "media") };
            var anime = new Anime { Key = "frieren", Title = "Frieren" };
            var first = new Episode { AnimeId = anime.Id, SeasonNumber = 1, Number = 1, Title = "Episode 1" };
            var second = new Episode { AnimeId = anime.Id, SeasonNumber = 1, Number = 2, Title = "Episode 2" };
            Db.LibraryRoots.Add(root);
            Db.Anime.Add(anime);
            Db.Episodes.AddRange(first, second);
            Db.MediaFiles.Add(new MediaFile { LibraryRootId = root.Id, EpisodeId = first.Id, Path = Path.Combine(root.Path, "Frieren", "e01.mkv"), SizeBytes = 1 });
            Db.AnimeMetadata.Add(new AnimeMetadata
            {
                AnimeId = anime.Id,
                Provider = "anilist",
                ExternalId = "154587",
                PreferredTitle = "Frieren: Beyond Journey's End",
                Status = "RELEASING"
            });
            await Db.SaveChangesAsync();
            EpisodeWithFile = first.Id;
            return anime;
        }

        public async Task<Guid> AddNovelAsync()
        {
            var work = new NovelWork
            {
                SourceProvider = "syosetu",
                SourceKey = "n0000aa",
                SourceUrl = "https://ncode.syosetu.com/n0000aa/",
                Title = "Novel",
                MetadataProvider = "anilist",
                MetadataExternalId = "999",
                MetadataStatus = "NOT_YET_RELEASED"
            };
            Db.NovelWorks.Add(work);
            await Db.SaveChangesAsync();
            return work.Id;
        }

        public async Task<Guid> AddBookAsync(string publishedDate)
        {
            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "upload-" + Guid.NewGuid().ToString("N"),
                SourceUrl = "upload://book.epub",
                Title = "Book"
            };
            var edition = new BookEdition
            {
                WorkId = work.Id,
                EditionKey = "edition-" + Guid.NewGuid().ToString("N")[..8],
                Language = "en",
                PublishedDate = publishedDate,
                IsPrimary = true
            };
            Db.NovelWorks.Add(work);
            Db.BookEditions.Add(edition);
            Db.BookFiles.Add(new BookFile
            {
                EditionId = edition.Id,
                FileKey = "sha256-" + Guid.NewGuid().ToString("N"),
                FileName = "book.epub",
                ContentHash = new string('A', 64),
                SizeBytes = 1,
                IsPrimary = true
            });
            await Db.SaveChangesAsync();
            return work.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
