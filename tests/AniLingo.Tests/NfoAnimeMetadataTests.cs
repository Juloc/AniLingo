using System.Net;
using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

// Unit-level coverage for the AnimeMetadataService additions that read local NFO data: season-level
// AniList IDs feeding the episode-range mapping flow, and MyAnimeList-ID-to-AniList resolution.
// NFO parsing and scan-level wiring are covered by NfoReaderTests and NfoLibraryScanTests.
[TestClass]
public sealed class NfoAnimeMetadataTests
{
    [TestMethod]
    public async Task SeasonAniListIdMapsEpisodeRangeWhenNoMappingExistsForThatSeason()
    {
        var provider = new FakeProvider();
        provider.Add("500", "Season Two Entry", episodeCount: 12);
        await using var fixture = await Fixture.CreateAsync(provider);
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 2, episodeCount: 12);

        var result = await fixture.Service.MatchSeasonAniListIdAsync(
            animeId,
            2,
            "500",
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        var mappings = await fixture.AccountStore.LoadEpisodeMappingsAsync(animeId, CancellationToken.None);
        Assert.AreEqual(1, mappings.Count);
        Assert.AreEqual(2, mappings[0].SeasonNumber);
        Assert.AreEqual(1, mappings[0].LocalEpisodeStart);
        Assert.AreEqual(12, mappings[0].LocalEpisodeEnd);
        Assert.AreEqual("500", mappings[0].ExternalId);
        Assert.IsNull(await fixture.ReviewStore.FindPendingAsync(
            "anime",
            animeId.ToString(),
            cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task SeasonAniListIdNeverOverwritesAnExistingMappingForThatSeason()
    {
        var provider = new FakeProvider();
        provider.Add("500", "Season Two Entry", episodeCount: 12);
        await using var fixture = await Fixture.CreateAsync(provider);
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 2, episodeCount: 12);

        Assert.IsTrue(await fixture.AccountStore.TryAddEpisodeMappingAsync(
            new AnimeEpisodeMetadataMapping(
                Guid.NewGuid(),
                animeId,
                2,
                1,
                12,
                1,
                AniListMetadataProvider.ProviderKey,
                "999",
                "Manually chosen entry",
                12,
                DateTimeOffset.UtcNow),
            CancellationToken.None));

        var result = await fixture.Service.MatchSeasonAniListIdAsync(
            animeId,
            2,
            "500",
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        var mappings = await fixture.AccountStore.LoadEpisodeMappingsAsync(animeId, CancellationToken.None);
        Assert.AreEqual(1, mappings.Count);
        Assert.AreEqual("999", mappings[0].ExternalId);
        Assert.IsNull(await fixture.ReviewStore.FindPendingAsync(
            "anime",
            animeId.ToString(),
            cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task UnknownSeasonAniListIdCreatesAMappingReviewTaskInsteadOfBeingDropped()
    {
        await using var fixture = await Fixture.CreateAsync(new FakeProvider());
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 2, episodeCount: 12);

        var result = await fixture.Service.MatchSeasonAniListIdAsync(
            animeId,
            2,
            "unknown-id",
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        var task = await fixture.ReviewStore.FindPendingAsync(
            "anime",
            animeId.ToString(),
            cancellationToken: CancellationToken.None);
        Assert.IsNotNull(task);
        Assert.AreEqual("episode-ranges:season-2", task.Purpose);
    }

    [TestMethod]
    public async Task MalIdIsResolvedToAniListForANewlyUnmatchedAnime()
    {
        var http = new FakeAniListHttp();
        var provider = new AniListMetadataProvider(
            new HttpClient(http) { BaseAddress = new Uri("https://graphql.anilist.co/") },
            NullLogger<AniListMetadataProvider>.Instance);
        await using var fixture = await Fixture.CreateAsync(provider);
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 1, episodeCount: 1);

        var result = await fixture.Service.MatchNfoProviderIdsAsync(
            animeId,
            aniListId: null,
            myAnimeListId: "9994",
            CancellationToken.None);

        Assert.IsTrue(result.Success, result.Error);
        var metadata = await fixture.Db.AnimeMetadata.AsNoTracking().SingleAsync();
        Assert.AreEqual("9001", metadata.ExternalId);
        Assert.AreEqual(AniListMetadataProvider.ProviderKey, metadata.Provider);
        Assert.IsTrue(http.Requests.Any(x => x.Contains("idMal")));
    }

    [TestMethod]
    public async Task MalIdLookupIsNeverUsedWhenTheAnimeAlreadyHasAMatch()
    {
        var http = new FakeAniListHttp();
        var provider = new AniListMetadataProvider(
            new HttpClient(http) { BaseAddress = new Uri("https://graphql.anilist.co/") },
            NullLogger<AniListMetadataProvider>.Instance);
        await using var fixture = await Fixture.CreateAsync(provider);
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 1, episodeCount: 1);
        fixture.Db.AnimeMetadata.Add(new AnimeMetadata
        {
            AnimeId = animeId,
            Provider = AniListMetadataProvider.ProviderKey,
            ExternalId = "111",
            PreferredTitle = "Manually chosen entry"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.MatchNfoProviderIdsAsync(
            animeId,
            aniListId: null,
            myAnimeListId: "9994",
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, http.Requests.Count);
        Assert.AreEqual(
            "111",
            (await fixture.Db.AnimeMetadata.AsNoTracking().SingleAsync()).ExternalId);
    }

    // dotnet ef migrations has-pending-model-changes is the authoritative check (run as part of
    // this change's validation); this test instead confirms the AddAnimeLocalMetadata migration
    // actually produces a schema the entity can round-trip through, including the cascade delete
    // from Anime and every NFO-derived field.
    [TestMethod]
    public async Task AnimeLocalMetadataMigrationProducesARoundTrippableSchema()
    {
        await using var fixture = await Fixture.CreateAsync(new FakeProvider());
        var animeId = await fixture.AddAnimeWithEpisodesAsync(seasonNumber: 1, episodeCount: 1);

        fixture.Db.AnimeLocalMetadata.Add(new AnimeLocalMetadata
        {
            AnimeId = animeId,
            OriginalTitle = "葬送のフリーレン",
            Plot = "An elf mage outlives her party.",
            Year = 2023,
            Premiered = new DateOnly(2023, 9, 29),
            MyAnimeListId = "52991",
            TvdbId = "424536",
            TmdbId = "209867",
            ImdbId = "tt22248376",
            SourceFileSizeBytes = 512,
            SourceFileLastWriteTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Db.SaveChangesAsync();

        var stored = await fixture.Db.AnimeLocalMetadata.AsNoTracking().SingleAsync(x => x.AnimeId == animeId);
        Assert.AreEqual(AnimeLocalMetadata.SourceNfo, stored.Source);
        Assert.AreEqual("葬送のフリーレン", stored.OriginalTitle);
        Assert.AreEqual("tt22248376", stored.ImdbId);
        Assert.AreEqual(new DateOnly(2023, 9, 29), stored.Premiered);

        // Deleting the anime must cascade to its local metadata row.
        fixture.Db.Anime.Remove(await fixture.Db.Anime.SingleAsync(x => x.Id == animeId));
        await fixture.Db.SaveChangesAsync();
        Assert.IsFalse(await fixture.Db.AnimeLocalMetadata.AnyAsync(x => x.AnimeId == animeId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string tempRoot,
            AppDbContext db,
            AnimeMetadataService service,
            AniListAccountStore accountStore,
            MediaMappingReviewStore reviewStore)
        {
            TempRoot = tempRoot;
            Db = db;
            Service = service;
            AccountStore = accountStore;
            ReviewStore = reviewStore;
        }

        public string TempRoot { get; }
        public AppDbContext Db { get; }
        public AnimeMetadataService Service { get; }
        public AniListAccountStore AccountStore { get; }
        public MediaMappingReviewStore ReviewStore { get; }

        public static async Task<Fixture> CreateAsync(IAnimeMetadataProvider provider)
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-nfo-metadata-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(tempRoot, "anilingo.db")};Foreign Keys=True")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var dataProtection = DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(tempRoot, "keys")));
            var accountStore = new AniListAccountStore(
                dataProtection,
                NullLogger<AniListAccountStore>.Instance,
                new DirectoryInfo(tempRoot));
            var reviewStore = new MediaMappingReviewStore(
                NullLogger<MediaMappingReviewStore>.Instance,
                new DirectoryInfo(tempRoot));
            var service = new AnimeMetadataService(db, [provider], accountStore, reviewStore);

            return new Fixture(
                tempRoot,
                db,
                service,
                accountStore,
                reviewStore);
        }

        public async Task<Guid> AddAnimeWithEpisodesAsync(int seasonNumber, int episodeCount)
        {
            var anime = new Anime { Key = Guid.NewGuid().ToString(), Title = "Test Anime" };
            Db.Anime.Add(anime);
            for (var number = 1; number <= episodeCount; number++)
            {
                Db.Episodes.Add(new Episode
                {
                    AnimeId = anime.Id,
                    SeasonNumber = seasonNumber,
                    Number = number,
                    Title = $"Episode {number}"
                });
            }

            await Db.SaveChangesAsync();
            return anime.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    private sealed class FakeProvider : IAnimeMetadataProvider
    {
        private readonly Dictionary<string, AnimeMetadataCandidate> entries = new(StringComparer.Ordinal);

        public string Key => AniListMetadataProvider.ProviderKey;

        public void Add(string externalId, string title, int? episodeCount = null) =>
            entries[externalId] = new AnimeMetadataCandidate(
                AniListMetadataProvider.ProviderKey,
                externalId,
                title,
                null,
                title,
                null,
                null,
                null,
                null,
                "TV",
                "FINISHED",
                "FALL",
                2023,
                episodeCount,
                24);

        public Task<IReadOnlyList<AnimeMetadataCandidate>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnimeMetadataCandidate>>([]);

        public Task<AnimeMetadataCandidate?> GetAsync(
            string externalId,
            CancellationToken cancellationToken) =>
            Task.FromResult(entries.GetValueOrDefault(externalId));
    }

    // Stands in for AniList's GraphQL endpoint: resolves a MyAnimeList ID lookup ("idMal") to a
    // fixed AniList entry, and answers the follow-up by-ID lookup MatchAsync performs with the
    // same entry so the full MAL -> AniList match completes.
    private sealed class FakeAniListHttp : HttpMessageHandler
    {
        private const string MediaJson =
            """
            {"id":9001,"title":{"romaji":"Resolved From MAL","english":null,"native":null},"description":null,"coverImage":null,"bannerImage":null,"format":"TV","status":"FINISHED","season":"FALL","seasonYear":2023,"episodes":24,"duration":24,"isAdult":false}
            """;

        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(body);

            if (body.Contains("idMal", StringComparison.Ordinal) ||
                body.Contains("Media(id: $id", StringComparison.Ordinal))
            {
                return Json("{\"data\":{\"Media\":" + MediaJson + "}}");
            }

            return Json("""{"data":{"Media":null}}""");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }
}
