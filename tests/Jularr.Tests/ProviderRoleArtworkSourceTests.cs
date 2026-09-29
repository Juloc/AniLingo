using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

// Issue #568: LibraryScanner/AnimeRepairService used to always pass an anime's stored
// CoverImageUrl/BannerImageUrl on to AnimeArtworkLibrary.ReconcileAsync, regardless of which
// provider actually supplied them. AnimeArtworkSourceResolver now only hands those URLs over when
// they match the anime's resolved Artwork role, without touching the artwork write path itself.
[TestClass]
public sealed class ProviderRoleArtworkSourceTests
{
    [TestMethod]
    public void Resolve_WhenArtworkRoleMatchesTheMetadataProvider_UsesItsImages()
    {
        var artwork = AnimeArtworkSourceResolver.Resolve(
            metadataProvider: MappingProviders.AniList,
            coverImageUrl: "https://example.invalid/cover.jpg",
            bannerImageUrl: "https://example.invalid/banner.jpg",
            resolvedArtworkProvider: MappingProviders.AniList);

        Assert.IsNotNull(artwork);
        Assert.AreEqual("https://example.invalid/cover.jpg", artwork.PosterUrl);
        Assert.AreEqual("https://example.invalid/banner.jpg", artwork.BannerUrl);
    }

    [TestMethod]
    public void Resolve_WhenArtworkRoleNamesADifferentProvider_ReturnsNoProviderArtwork()
    {
        var artwork = AnimeArtworkSourceResolver.Resolve(
            metadataProvider: MappingProviders.AniList,
            coverImageUrl: "https://example.invalid/cover.jpg",
            bannerImageUrl: "https://example.invalid/banner.jpg",
            resolvedArtworkProvider: MappingProviders.Tmdb);

        Assert.IsNull(artwork);
    }

    [TestMethod]
    public async Task ResolveForAnime_WithNoRoleConfigured_ReproducesTodaysBehaviour()
    {
        await using var fixture = await Fixture.CreateAsync();
        var animeId = Guid.NewGuid();

        var artwork = await AnimeArtworkSourceResolver.ResolveForAnimeAsync(
            fixture.Db,
            animeId,
            metadataProvider: MappingProviders.AniList,
            coverImageUrl: "https://example.invalid/cover.jpg",
            bannerImageUrl: "https://example.invalid/banner.jpg",
            CancellationToken.None);

        Assert.IsNotNull(artwork, "Artwork's built-in default is AniList, matching the metadata provider.");
    }

    [TestMethod]
    public async Task ResolveForAnime_WithArtworkReassigned_StopsUsingTheMismatchedProviderImages()
    {
        await using var fixture = await Fixture.CreateAsync();
        var animeId = Guid.NewGuid();
        await fixture.Roles.SetWorkOverrideAsync(
            animeId, MappingProviderRole.Artwork, MappingProviders.Tmdb, CancellationToken.None);

        var artwork = await AnimeArtworkSourceResolver.ResolveForAnimeAsync(
            fixture.Db,
            animeId,
            metadataProvider: MappingProviders.AniList,
            coverImageUrl: "https://example.invalid/cover.jpg",
            bannerImageUrl: "https://example.invalid/banner.jpg",
            CancellationToken.None);

        Assert.IsNull(artwork, "AniList images must not be used once Artwork points at Tmdb.");

        // (c) A different anime, and a different role for the same anime, are unaffected.
        var otherAnime = Guid.NewGuid();
        var otherArtwork = await AnimeArtworkSourceResolver.ResolveForAnimeAsync(
            fixture.Db,
            otherAnime,
            metadataProvider: MappingProviders.AniList,
            coverImageUrl: "https://example.invalid/cover.jpg",
            bannerImageUrl: "https://example.invalid/banner.jpg",
            CancellationToken.None);
        Assert.IsNotNull(otherArtwork);

        var display = await fixture.Roles.ResolveRoleForWorkAsync(
            animeId, MappingProviderRole.DisplayMetadata, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, display.Provider);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string databasePath;

        private Fixture(string databasePath, AppDbContext db)
        {
            this.databasePath = databasePath;
            Db = db;
            Roles = new ProviderRoleAssignmentStore(db);
        }

        public AppDbContext Db { get; }
        public ProviderRoleAssignmentStore Roles { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jularr-artwork-roles-{Guid.NewGuid():N}.db");
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(databasePath, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
