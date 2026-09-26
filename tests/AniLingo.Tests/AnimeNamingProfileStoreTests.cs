using AniLingo.Web.Features.Acquisition.Naming;

namespace AniLingo.Tests;

[TestClass]
public sealed class AnimeNamingProfileStoreTests
{
    [TestMethod]
    public async Task ArbitraryProfilesPersistAndResolveAnimeOverLibraryOverDefault()
    {
        var directory = Directory.CreateTempSubdirectory("anilingo-naming-");
        try
        {
            var store = new AnimeNamingProfileStore(directory);
            var animeId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var custom = AnimeNamingPresets.SonarrDefault() with
            {
                Id = "custom",
                Name = "Custom",
                StandardEpisodeFormat = "{Series CleanTitle}.S{season:00}E{episode:00}.{Quality Title}",
                MultiEpisodeStyle = AnimeMultiEpisodeStyle.Scene,
                ColonReplacement = AnimeColonReplacement.SpaceDashSpace
            };

            Assert.AreEqual(AnimeNamingPresets.SonarrMediaInfoId, (await store.ResolveAsync(animeId, rootId)).Profile.Id);

            await store.UpsertAsync(custom);
            await store.AssignLibraryAsync(rootId, AnimeNamingPresets.SonarrDefaultId);
            var library = await store.ResolveAsync(animeId, rootId);
            Assert.AreEqual(AnimeNamingPresets.SonarrDefaultId, library.Profile.Id);
            Assert.AreEqual("library", library.Source);

            await store.AssignAnimeAsync(animeId, "custom", AnimeSeriesType.Standard);
            var reloaded = new AnimeNamingProfileStore(directory);
            var anime = await reloaded.ResolveAsync(animeId, rootId);
            Assert.AreEqual(custom, anime.Profile);
            Assert.AreEqual(AnimeSeriesType.Standard, anime.SeriesType);
            Assert.AreEqual("anime", anime.Source);

            Assert.IsFalse(await reloaded.DeleteAsync("custom"), "An assigned profile cannot be deleted.");
            await reloaded.AssignAnimeAsync(animeId, null, AnimeSeriesType.Anime);
            Assert.IsTrue(await reloaded.DeleteAsync("custom"));
            Assert.AreEqual("default", (await reloaded.ResolveAsync(animeId, null)).Source);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidProfilesAreRejectedAndNothingIsWritten()
    {
        var directory = Directory.CreateTempSubdirectory("anilingo-naming-");
        try
        {
            var store = new AnimeNamingProfileStore(directory);
            var invalid = AnimeNamingPresets.SonarrDefault() with { Id = "broken", StandardEpisodeFormat = "{Series Title}" };

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.UpsertAsync(invalid));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.SetDefaultAsync("missing"));
            Assert.IsFalse(File.Exists(Path.Combine(directory.FullName, AnimeNamingProfileStore.FileName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
