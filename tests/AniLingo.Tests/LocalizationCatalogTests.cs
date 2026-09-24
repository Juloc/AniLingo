using AniLingo.Web.Data;
using AniLingo.Web.Features.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class LocalizationCatalogTests
{
    [TestMethod]
    public void LocaleFallbackUsesExactThenParentThenEnglish()
    {
        CollectionAssert.AreEqual(
            new[] { "de-CH", "de", "en" },
            UiTranslationCatalog.BuildFallbackChain("de_CH").ToArray());
    }

    [TestMethod]
    public void ResourceHashIncludesTranslationContext()
    {
        var first = new UiMessageDefinition(
            "test.action",
            "Open",
            "Test",
            "Button",
            "Open the selected media item.",
            "concise action",
            16);

        var second = first with
        {
            Description = "Open the selected settings panel."
        };

        Assert.AreNotEqual(first.SourceHash, second.SourceHash);
    }

    [TestMethod]
    public void GeneratedTranslationMustPreservePlaceholders()
    {
        var message = new UiMessageDefinition(
            "test.count",
            "{count} items",
            "Test",
            "Status",
            "Number of items.",
            Placeholders: new Dictionary<string, string>
            {
                ["count"] = "Current item count."
            });

        Assert.IsTrue(UiTranslationCatalog.IsGeneratedTranslationValid(
            message,
            "{count} Einträge"));
        Assert.IsFalse(UiTranslationCatalog.IsGeneratedTranslationValid(
            message,
            "Einträge"));
    }

    [TestMethod]
    public async Task CatalogPersistsManualTranslationAndParentFallback()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Store.AddLocaleAsync("de", CancellationToken.None);

        await fixture.Store.SaveManualAsync(
            "de",
            "common.save",
            "Speichern",
            CancellationToken.None);

        var bundle = await fixture.Store.LoadBundleAsync(
            "de-DE",
            CancellationToken.None);

        Assert.AreEqual("Speichern", bundle["common.save"]);
    }

    [TestMethod]
    public async Task ProfileLocaleLoadsPersistedLocalizedBundle()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Store.AddLocaleAsync("de", CancellationToken.None);
        await fixture.Store.SaveManualAsync(
            "de",
            "common.save",
            "Speichern",
            CancellationToken.None);

        await fixture.Store.SetProfileLocaleAsync(
            "reader-1",
            "de",
            CancellationToken.None);

        var bundle = await fixture.Store.LoadProfileBundleAsync(
            "reader-1",
            CancellationToken.None);

        Assert.AreEqual("de", bundle.Locale);
        Assert.AreEqual("ltr", bundle.Direction);
        Assert.AreEqual("Speichern", bundle["common.save"]);
    }

    [TestMethod]
    public async Task AiGenerationCannotOverwriteManualTranslation()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Store.AddLocaleAsync("de", CancellationToken.None);

        await fixture.Store.SaveManualAsync(
            "de",
            "common.save",
            "Speichern",
            CancellationToken.None);

        await fixture.Store.SaveGeneratedAsync(
            "de",
            new UiTranslationGenerationResult(
                "test-ai",
                "test",
                UiTranslationCatalog.PromptVersion,
                [new UiGeneratedTranslation("common.save", "Sichern")]),
            CancellationToken.None);

        var bundle = await fixture.Store.LoadBundleAsync(
            "de",
            CancellationToken.None);

        Assert.AreEqual("Speichern", bundle["common.save"]);
        var entry = (await fixture.Store.GetEntriesAsync(
            "de",
            "common.save",
            CancellationToken.None)).Single();
        Assert.AreEqual(UiTranslationStatus.Manual, entry.Status);
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private CatalogFixture(
            string directory,
            AppDbContext db,
            UiTranslationCatalogStore store)
        {
            Directory = directory;
            Db = db;
            Store = store;
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public UiTranslationCatalogStore Store { get; }

        public static async Task<CatalogFixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-localization-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var database = Path.Combine(directory, "anilingo.db");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={database};Foreign Keys=True")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var store = new UiTranslationCatalogStore(db);
            await store.SyncSourceMessagesAsync(CancellationToken.None);

            return new CatalogFixture(directory, db, store);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
