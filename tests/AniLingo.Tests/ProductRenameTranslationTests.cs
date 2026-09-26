using AniLingo.Web.Data;
using AniLingo.Web.Features.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class ProductRenameTranslationTests
{
    private const string RenamedKey = "settings.language.description";
    private const string ChangedKey = "settings.subtitle";

    [TestMethod]
    public async Task TranslationsOfRenameOnlyChangesAreCarriedOverAndOtherChangesGoOutdated()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                .Options;
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var store = new UiTranslationCatalogStore(db);
            await store.SyncSourceMessagesAsync(CancellationToken.None);
            await store.AddLocaleAsync("de", CancellationToken.None);

            Assert.IsTrue(UiTranslationResources.TryGet(RenamedKey, out var renamed));
            StringAssert.Contains(renamed.DefaultText, "Jularr");

            // Simulate an installation upgraded from before the rebrand: the stored source rows still
            // carry "AniLingo" and the German translations are pinned to that old source hash.
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "UiTranslationMessages"
                SET "DefaultText" = replace("DefaultText", 'Jularr', 'AniLingo'),
                    "Description" = replace("Description", 'Jularr', 'AniLingo'),
                    "DoNotTranslateJson" = replace("DoNotTranslateJson", 'Jularr', 'AniLingo'),
                    "SourceHash" = 'pre-rebrand-' || "Key"
                WHERE "Key" = {0};
                """,
                RenamedKey);
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "UiTranslationMessages"
                SET "DefaultText" = 'An older, different subtitle.',
                    "SourceHash" = 'pre-rebrand-' || "Key"
                WHERE "Key" = {0};
                """,
                ChangedKey);
            await InsertGermanAsync(db, RenamedKey, "Wähle die Sprache, die AniLingo für Menüs verwendet.");
            await InsertGermanAsync(db, ChangedKey, "Ein älterer Untertitel.");

            await store.SyncSourceMessagesAsync(CancellationToken.None);

            var entries = (await store.GetEntriesAsync("de", null, CancellationToken.None))
                .ToDictionary(entry => entry.Message.Key);
            Assert.AreEqual("Wähle die Sprache, die Jularr für Menüs verwendet.", entries[RenamedKey].Text);
            Assert.AreEqual(UiTranslationStatus.Generated, entries[RenamedKey].Status);
            Assert.AreEqual(UiTranslationStatus.Outdated, entries[ChangedKey].Status);

            var bundle = await store.LoadLocaleBundleAsync(UiTranslationCatalog.ParseLocale("de"), CancellationToken.None);
            Assert.AreEqual("Wähle die Sprache, die Jularr für Menüs verwendet.", bundle[RenamedKey]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task InsertGermanAsync(AppDbContext db, string key, string text) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "UiTranslations" (
                "Locale", "MessageKey", "Text", "Status", "SourceHash",
                "Provider", "Model", "PromptVersion", "GeneratedAt", "ReviewedAt", "UpdatedAt")
            VALUES ('de', {0}, {1}, 'Generated', 'pre-rebrand-' || {0},
                'test', 'test', 1, '2026-09-01T00:00:00Z', NULL, '2026-09-01T00:00:00Z');
            """,
            key,
            text);
}
