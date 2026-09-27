using Jularr.Web.Features.Novels;
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

        Assert.ThrowsException<InvalidDataException>(
            () => ReadingSourceCatalog.Normalize(
                settings,
                rejectInvalidPriority: true));
    }

    [TestMethod]
    public void CatalogContainsOnlyImplementedAutomatedSources()
    {
        var keys = ReadingSourceCatalog.Definitions
            .Select(source => source.Key)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                NcodeNovelSourceProvider.ProviderKey,
                NovelAniListProvider.ProviderKey
            },
            keys);
    }
}
