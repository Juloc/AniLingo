using Jularr.Web.Features.Localization;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.ReadingDiscovery;
using Jularr.Web.Features.ReadingSources;

namespace Jularr.Tests;

[TestClass]
public sealed class ReadingSourceSettingsTests
{
    [TestMethod]
    public void DefaultsEnableImplementedSourcesWithStablePriority()
    {
        var settings = ReadingSourceSettingsState.Default;

        Assert.IsTrue(
            settings.IsEnabled(NcodeNovelSourceProvider.ProviderKey));
        Assert.IsTrue(
            settings.IsEnabled(NovelAniListProvider.ProviderKey));
        Assert.IsTrue(
            settings.PriorityFor(NcodeNovelSourceProvider.ProviderKey) <
            settings.PriorityFor(NovelAniListProvider.ProviderKey));
    }

    [TestMethod]
    public async Task SaveAndLoadPreservesSourceEnablementAndPriority()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jularr-reading-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ReadingSourceSettingsStore(root);
            var settings = new ReadingSourceSettingsState(
                new Dictionary<string, ReadingSourcePreference>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [NcodeNovelSourceProvider.ProviderKey] =
                        new(false, 90),
                    [NovelAniListProvider.ProviderKey] =
                        new(true, 5)
                });

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.IsFalse(
                loaded.IsEnabled(NcodeNovelSourceProvider.ProviderKey));
            Assert.AreEqual(
                90,
                loaded.PriorityFor(NcodeNovelSourceProvider.ProviderKey));
            Assert.IsTrue(
                loaded.IsEnabled(NovelAniListProvider.ProviderKey));
            Assert.AreEqual(
                5,
                loaded.PriorityFor(NovelAniListProvider.ProviderKey));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void NormalizeRejectsInvalidPriority()
    {
        var settings =
            new Dictionary<string, ReadingSourcePreference>(
                StringComparer.OrdinalIgnoreCase)
            {
                [NcodeNovelSourceProvider.ProviderKey] =
                    new(true, 0)
            };

        var rejected = false;
        try
        {
            _ = ReadingSourceCatalog.Normalize(
                settings,
                rejectInvalidPriority: true);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    [TestMethod]
    public void CatalogContainsOnlySourcesWithASupportedAccessPath()
    {
        var keys = ReadingSourceCatalog.Definitions
            .Select(source => source.Key)
            .ToArray();

        // JustLightNovels, EmpireNovel and NovelPing are deliberately absent: they rely on
        // unsupported scraping or have unclear distribution rights.
        CollectionAssert.AreEquivalent(
            new[]
            {
                NcodeNovelSourceProvider.ProviderKey,
                NovelAniListProvider.ProviderKey,
                BookWalkerCatalogProvider.ProviderKey,
                WebNovelCatalogProvider.ProviderKey,
                InternetArchiveCatalogProvider.ProviderKey
            },
            keys);
    }

    [TestMethod]
    public void NewSourcesAppearAfterTheExistingOnesByDefault()
    {
        var settings = ReadingSourceSettingsState.Default;
        var order = ReadingSourceCatalog.Definitions
            .OrderBy(source => settings.PriorityFor(source.Key))
            .Select(source => source.Key)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                NcodeNovelSourceProvider.ProviderKey,
                NovelAniListProvider.ProviderKey,
                BookWalkerCatalogProvider.ProviderKey,
                WebNovelCatalogProvider.ProviderKey,
                InternetArchiveCatalogProvider.ProviderKey
            },
            order);
    }

    [TestMethod]
    public void WebNovelIsOptInBecauseItBlocksAutomatedAccess()
    {
        var settings = ReadingSourceSettingsState.Default;

        Assert.IsFalse(settings.IsEnabled(WebNovelCatalogProvider.ProviderKey));
        Assert.IsTrue(settings.IsEnabled(BookWalkerCatalogProvider.ProviderKey));
        Assert.IsTrue(settings.IsEnabled(InternetArchiveCatalogProvider.ProviderKey));
    }

    [TestMethod]
    public async Task SettingsSavedBeforeTheNewSourcesGetTheirDefaults()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jularr-reading-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, "integrations");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "reading-sources.json"),
                """
                {
                  "providers": {
                    "syosetu": { "enabled": false, "priority": 70 },
                    "anilist": { "enabled": true, "priority": 3 }
                  }
                }
                """);

            var loaded = await new ReadingSourceSettingsStore(root).LoadAsync();

            Assert.IsFalse(loaded.IsEnabled(NcodeNovelSourceProvider.ProviderKey));
            Assert.AreEqual(70, loaded.PriorityFor(NcodeNovelSourceProvider.ProviderKey));
            Assert.AreEqual(3, loaded.PriorityFor(NovelAniListProvider.ProviderKey));
            Assert.IsTrue(loaded.IsEnabled(BookWalkerCatalogProvider.ProviderKey));
            Assert.IsFalse(loaded.IsEnabled(WebNovelCatalogProvider.ProviderKey));
            Assert.IsTrue(loaded.IsEnabled(InternetArchiveCatalogProvider.ProviderKey));
            Assert.AreEqual(
                ReadingSourceCatalog.GetRequired(InternetArchiveCatalogProvider.ProviderKey).DefaultPriority,
                loaded.PriorityFor(InternetArchiveCatalogProvider.ProviderKey));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void OnlyPublicFullTextAndPublishedEditionsCanBeAdded()
    {
        var addable = ReadingSourceCatalog.Definitions
            .Where(source => source.CanAdd)
            .Select(source => source.Key)
            .ToArray();

        // Reference, preview and metadata-only sources list results but can never be added
        // (the Light Novel page rejects them server-side through this same flag).
        CollectionAssert.AreEquivalent(
            new[]
            {
                NcodeNovelSourceProvider.ProviderKey,
                NovelAniListProvider.ProviderKey
            },
            addable);

        var bookWalker = ReadingSourceCatalog.GetRequired(BookWalkerCatalogProvider.ProviderKey);
        Assert.AreEqual(ReadingSourceCapabilities.Preview, bookWalker.PrimaryCapability);
        Assert.IsFalse(bookWalker.SupportsDirectImport);

        Assert.AreEqual(
            ReadingSourceCapabilities.ExternalReference,
            ReadingSourceCatalog.GetRequired(WebNovelCatalogProvider.ProviderKey).PrimaryCapability);
        Assert.AreEqual(
            ReadingSourceCapabilities.ExternalReference,
            ReadingSourceCatalog.GetRequired(InternetArchiveCatalogProvider.ProviderKey).PrimaryCapability);
        Assert.AreEqual(
            ReadingSourceCapabilities.PublicFullText,
            ReadingSourceCatalog.GetRequired(NcodeNovelSourceProvider.ProviderKey).PrimaryCapability);
        Assert.AreEqual(
            ReadingSourceCapabilities.Acquisition,
            ReadingSourceCatalog.GetRequired(NovelAniListProvider.ProviderKey).PrimaryCapability);
    }

    [TestMethod]
    public void OnlyDirectImportSourcesCanBuildAnImportUrl()
    {
        foreach (var source in ReadingSourceCatalog.Definitions)
        {
            Assert.AreEqual(
                source.SupportsDirectImport,
                source.DirectImportUrl is not null,
                $"{source.Key} import capability and import URL disagree.");
        }

        Assert.AreEqual(
            "https://ncode.syosetu.com/n9669bk/",
            ReadingSourceCatalog.GetRequired(NcodeNovelSourceProvider.ProviderKey)
                .DirectImportUrl!("N9669BK"));
    }

    [TestMethod]
    public void AddableSourcesValidateTheirExternalIds()
    {
        var syosetu = ReadingSourceCatalog.GetRequired(NcodeNovelSourceProvider.ProviderKey);
        Assert.IsTrue(syosetu.IsValidExternalId("n9669bk"));
        Assert.IsFalse(syosetu.IsValidExternalId("https://example.com"));
        Assert.IsFalse(syosetu.IsValidExternalId(""));

        var aniList = ReadingSourceCatalog.GetRequired(NovelAniListProvider.ProviderKey);
        Assert.IsTrue(aniList.IsValidExternalId("12345"));
        Assert.IsFalse(aniList.IsValidExternalId("0"));
        Assert.IsFalse(aniList.IsValidExternalId("abc"));
        Assert.IsFalse(aniList.IsValidExternalId(null));

        Assert.IsFalse(ReadingSourceCatalog.TryGet("justlightnovels", out _));
    }

    [TestMethod]
    public void EverySourceTextIsInTheUiCatalog()
    {
        var catalogKeys = UiTranslationResources.All
            .Select(message => message.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var source in ReadingSourceCatalog.Definitions)
        {
            foreach (var key in new[]
                     {
                         source.DescriptionKey,
                         source.LicensingKey,
                         source.CapabilityKey
                     })
            {
                Assert.IsTrue(catalogKeys.Contains(key), $"Missing UI resource {key}.");
            }
        }

        foreach (var capability in new[]
                 {
                     ReadingSourceCapabilities.PublicFullText,
                     ReadingSourceCapabilities.Acquisition,
                     ReadingSourceCapabilities.Preview,
                     ReadingSourceCapabilities.ExternalReference,
                     ReadingSourceCapabilities.Metadata
                 })
        {
            Assert.IsTrue(
                catalogKeys.Contains(ReadingSourceDefinition.CapabilityKeyFor(capability)));
        }

        foreach (var access in new[]
                 {
                     ReadingAccess.OpenLicense,
                     ReadingAccess.OpenUnverified,
                     ReadingAccess.Lendable
                 })
        {
            var key = new ReadingCatalogCandidate(
                "internetarchive", "x", "x", null, null, null, null, null, null, null, null,
                false, access).AccessKey;
            Assert.IsNotNull(key);
            Assert.IsTrue(catalogKeys.Contains(key), $"Missing UI resource {key}.");
        }

        foreach (var status in Enum.GetValues<ReadingSourceHealthStatus>())
        {
            var key = new ReadingSourceHealthSnapshot(status, null, null, null).UiKey;
            Assert.IsTrue(catalogKeys.Contains(key), $"Missing UI resource {key}.");
        }
    }
}
