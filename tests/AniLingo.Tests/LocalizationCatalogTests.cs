using System.Security.Claims;
using System.Text.RegularExpressions;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace AniLingo.Tests;

[TestClass]
public sealed partial class LocalizationCatalogTests
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

    [TestMethod]
    public void EveryResourceCarriesTranslationContext()
    {
        var duplicates = UiTranslationResources.All
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        Assert.AreEqual(0, duplicates.Length, string.Join(", ", duplicates));

        foreach (var message in UiTranslationResources.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.DefaultText), message.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.Feature), message.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.Surface), message.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.Description), message.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(message.Tone), message.Key);
            Assert.IsTrue(message.MaxLength is > 0, $"{message.Key} needs max-length guidance.");
            Assert.IsTrue(
                message.DefaultText.Length <= message.MaxLength,
                $"{message.Key} source text exceeds its own max length.");

            foreach (Match placeholder in Regex.Matches(message.DefaultText, @"\{([A-Za-z0-9_.-]+)\}"))
            {
                Assert.IsTrue(
                    message.Placeholders?.ContainsKey(placeholder.Groups[1].Value) == true,
                    $"{message.Key} must describe placeholder {placeholder.Value}.");
            }
        }
    }

    [TestMethod]
    public void EveryLiteralUiKeyUsedBySourceExistsInCatalog()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "AniLingo.Web");
        var files = Directory
            .EnumerateFiles(sourceRoot, "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var missing = new List<string>();
        var checkedKeys = 0;
        foreach (var file in files)
        {
            foreach (Match match in UiKeyUsage().Matches(File.ReadAllText(file)))
            {
                checkedKeys++;
                var key = match.Groups["key"].Value;
                if (!UiTranslationResources.TryGet(key, out _))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.IsTrue(checkedKeys > 50, "The key-usage scan did not find the expected catalog lookups.");
        Assert.AreEqual(0, missing.Count, "Missing catalog keys: " + string.Join(", ", missing.Distinct()));
    }

    [TestMethod]
    public void BrowserLocaleSelectionPrefersEnabledLocalesInQualityOrder()
    {
        UiLocaleMetadata[] enabled =
        [
            UiTranslationCatalog.ParseLocale("en"),
            UiTranslationCatalog.ParseLocale("de"),
            UiTranslationCatalog.ParseLocale("id")
        ];

        Assert.AreEqual("de", Select("de-DE,de;q=0.9,en;q=0.8", enabled));
        Assert.AreEqual("id", Select("id-ID", enabled));
        Assert.AreEqual("de", Select("fr-FR,fr;q=0.9,de;q=0.7", enabled));
        Assert.AreEqual("id", Select("de;q=0.4,id;q=0.9", enabled));
        Assert.AreEqual("en", Select("en-GB,de;q=0.5", enabled));
        Assert.AreEqual("id", Select("de;q=0,id;q=0.1", enabled));
    }

    [TestMethod]
    public void BrowserLocaleSelectionFallsBackToEnglish()
    {
        UiLocaleMetadata[] enabled =
        [
            UiTranslationCatalog.ParseLocale("en"),
            UiTranslationCatalog.ParseLocale("de")
        ];

        Assert.AreEqual("en", Select("ja-JP,fr;q=0.8", enabled));
        Assert.AreEqual("en", Select("*", enabled));
        Assert.AreEqual("en", Select("not a language tag", enabled));
        Assert.AreEqual("en", Select(null, enabled));
        Assert.AreEqual("en", Select("de-DE", []));
    }

    [TestMethod]
    public async Task AnonymousRequestUsesBrowserLanguageOnlyWhenOwnerEnabledIt()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var beforeEnabled = await UiRequestLocalization.GetBundleAsync(
            AnonymousRequest("de-DE,de;q=0.9"),
            fixture.Db);
        Assert.AreEqual("en", beforeEnabled.Locale);
        Assert.AreEqual("Sign in", beforeEnabled["account.login.title"]);

        await fixture.Store.AddLocaleAsync("de", CancellationToken.None);
        await fixture.Store.SaveManualAsync(
            "de",
            "account.login.title",
            "Anmelden",
            CancellationToken.None);

        var context = AnonymousRequest("de-DE,de;q=0.9");
        var bundle = await UiRequestLocalization.GetBundleAsync(context, fixture.Db);

        Assert.AreEqual("de", bundle.Locale);
        Assert.AreEqual("ltr", bundle.Direction);
        Assert.AreEqual("Anmelden", bundle["account.login.title"]);
        Assert.AreEqual("User name", bundle["account.field.userName"]);
        StringAssert.Contains(context.Response.Headers.Vary.ToString(), "Accept-Language");
        Assert.AreSame(
            bundle,
            await UiRequestLocalization.GetBundleAsync(context, fixture.Db),
            "The request bundle must be resolved once per request.");
    }

    [TestMethod]
    public async Task SignedInRequestUsesProfileLocaleInsteadOfBrowserLanguage()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.Store.AddLocaleAsync("de", CancellationToken.None);
        await fixture.Store.AddLocaleAsync("id", CancellationToken.None);
        await fixture.Store.SetProfileLocaleAsync("reader-1", "id", CancellationToken.None);

        var context = AnonymousRequest("de-DE");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "reader-1")],
            "test"));

        var bundle = await UiRequestLocalization.GetBundleAsync(context, fixture.Db);

        Assert.AreEqual("id", bundle.Locale);
    }

    private static string Select(string? acceptLanguage, IReadOnlyList<UiLocaleMetadata> enabled)
    {
        var headers = new HeaderDictionary();
        if (acceptLanguage is not null)
        {
            headers[HeaderNames.AcceptLanguage] = acceptLanguage;
        }

        return UiRequestLocalization.SelectBrowserLocale(
            new RequestHeaders(headers).AcceptLanguage,
            enabled).Locale;
    }

    private static DefaultHttpContext AnonymousRequest(string acceptLanguage)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = acceptLanguage;
        return context;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }

    [GeneratedRegex(@"\b[Uu]i(?:\.Format\(|\[)""(?<key>[A-Za-z0-9_.]+)""\s*[\],)]")]
    private static partial Regex UiKeyUsage();

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
