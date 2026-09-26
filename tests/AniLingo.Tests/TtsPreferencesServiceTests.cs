using AniLingo.Web.Data;
using AniLingo.Web.Features.Speech;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class TtsPreferencesServiceTests
{
    [TestMethod]
    public async Task DefaultsAreAutoProviderWithNoVoicesAndNeutralProsody()
    {
        var path = TempDatabasePath();
        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var service = new TtsPreferencesService(db, EpisodeFlowFixture.Account("profile-1"));

            var snapshot = await service.GetAsync();

            Assert.AreEqual("auto", snapshot.ProviderId);
            Assert.AreEqual(0, snapshot.VoiceIds.Count);
            Assert.AreEqual(1, snapshot.Rate);
            Assert.AreEqual(1, snapshot.Pitch);
            Assert.AreEqual(1, snapshot.Volume);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task PartialUpdateOnlyChangesProvidedFields()
    {
        var path = TempDatabasePath();
        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var service = new TtsPreferencesService(db, EpisodeFlowFixture.Account("profile-1"));

            await service.UpdateAsync(new TtsPreferencesUpdate(
                ProviderId: "device",
                Rate: 1.4,
                Pitch: null,
                Volume: null,
                VoiceLanguage: null,
                VoiceId: null));

            var afterFirstUpdate = await service.GetAsync();
            Assert.AreEqual("device", afterFirstUpdate.ProviderId);
            Assert.AreEqual(1.4, afterFirstUpdate.Rate);
            Assert.AreEqual(1, afterFirstUpdate.Pitch);

            await service.UpdateAsync(new TtsPreferencesUpdate(
                ProviderId: null,
                Rate: null,
                Pitch: 0.8,
                Volume: null,
                VoiceLanguage: null,
                VoiceId: null));

            var afterSecondUpdate = await service.GetAsync();
            Assert.AreEqual("device", afterSecondUpdate.ProviderId, "Untouched fields must survive a partial update.");
            Assert.AreEqual(1.4, afterSecondUpdate.Rate);
            Assert.AreEqual(0.8, afterSecondUpdate.Pitch);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task VoiceCanBeSetAndClearedPerLanguageWithoutAffectingOtherLanguages()
    {
        var path = TempDatabasePath();
        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var service = new TtsPreferencesService(db, EpisodeFlowFixture.Account("profile-1"));

            await service.UpdateAsync(new TtsPreferencesUpdate(
                VoiceLanguage: "ja-JP", VoiceId: "ja-voice"));
            await service.UpdateAsync(new TtsPreferencesUpdate(
                VoiceLanguage: "de_de", VoiceId: "de-voice"));

            var withBothVoices = await service.GetAsync();
            Assert.AreEqual("ja-voice", withBothVoices.VoiceIds["ja-JP"]);
            Assert.AreEqual("de-voice", withBothVoices.VoiceIds["de-DE"]);

            await service.UpdateAsync(new TtsPreferencesUpdate(
                VoiceLanguage: "ja-JP", VoiceId: null));

            var afterClear = await service.GetAsync();
            Assert.IsFalse(afterClear.VoiceIds.ContainsKey("ja-JP"));
            Assert.AreEqual("de-voice", afterClear.VoiceIds["de-DE"], "Clearing one language must not affect another.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ValuesAreNormalizedThroughTheSharedReaderPreferenceRules()
    {
        var path = TempDatabasePath();
        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var service = new TtsPreferencesService(db, EpisodeFlowFixture.Account("profile-1"));

            var snapshot = await service.UpdateAsync(new TtsPreferencesUpdate(
                ProviderId: "not-a-real-provider",
                Rate: 100,
                Pitch: -5,
                Volume: 9));

            Assert.AreEqual("auto", snapshot.ProviderId, "Unknown provider ids fall back to auto.");
            Assert.AreEqual(2.5, snapshot.Rate);
            Assert.AreEqual(.5, snapshot.Pitch);
            Assert.AreEqual(1d, snapshot.Volume);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"anilingo-tts-preferences-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
