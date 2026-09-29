using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Mapping;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Tracking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

// Issue #568: AnimeMetadataService used to always call AniList directly for automatic identity
// matching (DisplayMetadata) and always assumed AniList for episode-range/season.nfo mapping
// (EpisodeStructure). These tests prove: (a) an anime with nothing configured for either role
// behaves exactly as before, (b) an anime whose role was explicitly reassigned uses the
// configured provider for that concern only, and (c) reassigning one role never disturbs the
// others resolved for the same anime.
[TestClass]
public sealed class AnimeMetadataServiceProviderRoleTests
{
    [TestMethod]
    public async Task AutoMatch_WithNoRoleConfigured_SearchesAniListLikeToday()
    {
        await using var fixture = await Fixture.CreateAsync();
        var aniList = FakeMetadataProvider.AniList(
            new AnimeMetadataCandidate(
                MappingProviders.AniList, "154587", "Sousou no Frieren",
                "Sousou no Frieren", "Frieren: Beyond Journey's End", "葬送のフリーレン",
                null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24));
        var tvdb = FakeMetadataProvider.Tvdb(
            new AnimeMetadataCandidate(
                MappingProviders.Tvdb, "999", "Sousou no Frieren",
                "Sousou no Frieren", null, null, null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24));
        var animeId = await fixture.AddAnimeAsync("Sousou no Frieren", episodes: 12);

        var decision = await fixture.Metadata(aniList, tvdb).AutoMatchAsync(animeId, CancellationToken.None);

        Assert.AreEqual(AutomaticMediaMatchDisposition.Auto, decision.Disposition);
        Assert.AreEqual(1, aniList.SearchCalls, "Nothing configured must still search AniList, as before #568.");
        Assert.AreEqual(0, tvdb.SearchCalls, "Tvdb must not be consulted while DisplayMetadata is unset.");

        var stored = await fixture.Metadata(aniList, tvdb).GetAsync(animeId, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, stored!.Provider);
        Assert.AreEqual("154587", stored.ExternalId);
    }

    [TestMethod]
    public async Task AutoMatch_WithDisplayMetadataReassigned_SearchesTheConfiguredProviderOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var aniList = FakeMetadataProvider.AniList(
            new AnimeMetadataCandidate(
                MappingProviders.AniList, "154587", "Sousou no Frieren",
                "Sousou no Frieren", "Frieren: Beyond Journey's End", "葬送のフリーレン",
                null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24));
        var tvdb = FakeMetadataProvider.Tvdb(
            new AnimeMetadataCandidate(
                MappingProviders.Tvdb, "999", "Sousou no Frieren",
                "Sousou no Frieren", null, null, null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24));
        var animeId = await fixture.AddAnimeAsync("Sousou no Frieren", episodes: 12);

        await fixture.Roles.SetWorkOverrideAsync(
            animeId, MappingProviderRole.DisplayMetadata, MappingProviders.Tvdb, CancellationToken.None);

        var decision = await fixture.Metadata(aniList, tvdb).AutoMatchAsync(animeId, CancellationToken.None);

        Assert.AreEqual(AutomaticMediaMatchDisposition.Auto, decision.Disposition);
        Assert.AreEqual(0, aniList.SearchCalls, "AniList must not be contacted once DisplayMetadata points elsewhere.");
        Assert.AreEqual(1, tvdb.SearchCalls);

        var stored = await fixture.Metadata(aniList, tvdb).GetAsync(animeId, CancellationToken.None);
        Assert.AreEqual(MappingProviders.Tvdb, stored!.Provider);
        Assert.AreEqual("999", stored.ExternalId);

        // (c) Other concerns for the very same anime are unaffected by the DisplayMetadata override.
        var progress = await fixture.Roles.ResolveRoleForWorkAsync(
            animeId, MappingProviderRole.ProgressTracking, CancellationToken.None);
        var artwork = await fixture.Roles.ResolveRoleForWorkAsync(
            animeId, MappingProviderRole.Artwork, CancellationToken.None);
        var structure = await fixture.Roles.ResolveRoleForWorkAsync(
            animeId, MappingProviderRole.EpisodeStructure, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, progress.Provider);
        Assert.AreEqual(MappingProviders.AniList, artwork.Provider);
        Assert.AreEqual(MappingProviders.Local, structure.Provider);
    }

    [TestMethod]
    public async Task AutoMatch_WhenTheConfiguredProviderIsNotRegistered_DegradesGracefully()
    {
        // No IAnimeMetadataProvider is registered at all here; production always registers
        // AniListMetadataProvider, so this only exercises the graceful-degradation path (no
        // provider crash) rather than a scenario Jularr can hit today.
        await using var fixture = await Fixture.CreateAsync();
        var animeId = await fixture.AddAnimeAsync("Orphan Anime", episodes: 3);

        var decision = await fixture.Metadata().AutoMatchAsync(animeId, CancellationToken.None);

        Assert.AreEqual(AutomaticMediaMatchDisposition.None, decision.Disposition);
        StringAssert.Contains(decision.Evidence.Single(), "anilist");
        StringAssert.Contains(decision.Evidence.Single(), "not available");
    }

    [TestMethod]
    public async Task AutoMapEpisodeRanges_WithNoRoleConfigured_BehavesLikeTodayWhenAniListIsUnavailable()
    {
        // Reproduces the exact pre-#568 skip: metadata already matched to AniList, but no concrete
        // AniListMetadataProvider is registered to run the sequence query, so mapping is skipped.
        await using var fixture = await Fixture.CreateAsync();
        var animeId = await fixture.AddAnimeAsync("Sousou no Frieren", episodes: 12);
        await fixture.Metadata().MatchAsync(
            animeId,
            new AnimeMetadataCandidate(
                MappingProviders.AniList, "154587", "Sousou no Frieren",
                "Sousou no Frieren", null, null, null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24),
            CancellationToken.None);

        var result = await fixture.Metadata().AutoMapEpisodeRangesAsync(animeId, CancellationToken.None);

        Assert.IsFalse(result.Applied);
        Assert.AreEqual("The AniList metadata provider is not available.", result.Reason);
    }

    [TestMethod]
    public async Task AutoMapEpisodeRanges_WithEpisodeStructureReassigned_SkipsBeforeTouchingAniList()
    {
        await using var fixture = await Fixture.CreateAsync();
        var animeId = await fixture.AddAnimeAsync("Sousou no Frieren", episodes: 12);
        await fixture.Metadata().MatchAsync(
            animeId,
            new AnimeMetadataCandidate(
                MappingProviders.AniList, "154587", "Sousou no Frieren",
                "Sousou no Frieren", null, null, null, null, null, "TV", "FINISHED", "FALL", 2023, 12, 24),
            CancellationToken.None);
        await fixture.Roles.SetWorkOverrideAsync(
            animeId, MappingProviderRole.EpisodeStructure, MappingProviders.Tvdb, CancellationToken.None);

        var result = await fixture.Metadata().AutoMapEpisodeRangesAsync(animeId, CancellationToken.None);

        Assert.IsFalse(result.Applied);
        StringAssert.Contains(result.Reason, "tvdb");
        StringAssert.Contains(result.Reason, "not AniList");

        // (c) DisplayMetadata is unaffected: the anime's own AniList match is still readable.
        var stored = await fixture.Metadata().GetAsync(animeId, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, stored!.Provider);
    }

    [TestMethod]
    public async Task MatchSeasonAniListId_WithEpisodeStructureReassigned_IsNotApplied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var animeId = await fixture.AddAnimeAsync("Sousou no Frieren", episodes: 12);
        await fixture.Roles.SetWorkOverrideAsync(
            animeId, MappingProviderRole.EpisodeStructure, MappingProviders.Local, CancellationToken.None);

        var result = await fixture.Metadata().MatchSeasonAniListIdAsync(
            animeId, seasonNumber: 1, aniListId: "154587", CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "local");
    }

    private sealed class FakeMetadataProvider(string key, params AnimeMetadataCandidate[] results) : IAnimeMetadataProvider
    {
        public static FakeMetadataProvider AniList(params AnimeMetadataCandidate[] results) =>
            new(MappingProviders.AniList, results);

        public static FakeMetadataProvider Tvdb(params AnimeMetadataCandidate[] results) =>
            new(MappingProviders.Tvdb, results);

        public string Key => key;
        public int SearchCalls { get; private set; }

        public Task<IReadOnlyList<AnimeMetadataCandidate>> SearchAsync(
            string query, int limit, CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult<IReadOnlyList<AnimeMetadataCandidate>>(results);
        }

        public Task<AnimeMetadataCandidate?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(results.FirstOrDefault(x => x.ExternalId == externalId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly AniListAccountStore accountStore;
        private readonly MediaMappingReviewStore reviewStore;

        private Fixture(string root, AppDbContext db)
        {
            this.root = root;
            Db = db;
            Roles = new ProviderRoleAssignmentStore(db);
            accountStore = new AniListAccountStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AniListAccountStore>.Instance,
                new DirectoryInfo(root));
            reviewStore = new MediaMappingReviewStore(
                NullLogger<MediaMappingReviewStore>.Instance,
                new DirectoryInfo(Path.Combine(root, "anilist")));
        }

        public AppDbContext Db { get; }
        public ProviderRoleAssignmentStore Roles { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "jularr-tests",
                $"metadata-roles-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "anilist"));
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "jularr.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(root, db);
        }

        public AnimeMetadataService Metadata(params IAnimeMetadataProvider[] providers) =>
            new(Db, providers, accountStore, reviewStore);

        public async Task<Guid> AddAnimeAsync(string title, int episodes)
        {
            var anime = new Anime { Key = Guid.NewGuid().ToString("N"), Title = title };
            Db.Add(anime);
            for (var number = 1; number <= episodes; number++)
            {
                Db.Add(new Episode
                {
                    AnimeId = anime.Id,
                    SeasonNumber = 1,
                    Number = number,
                    Title = $"Episode {number}"
                });
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return anime.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
