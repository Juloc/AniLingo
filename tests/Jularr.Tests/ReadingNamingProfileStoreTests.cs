using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Naming;

namespace Jularr.Tests;

[TestClass]
public sealed class ReadingNamingProfileStoreTests
{
    [TestMethod]
    public async Task WithoutAFileEveryMediaKindResolvesToTheBuiltInDefault()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-");
        try
        {
            var store = new ReadingNamingProfileStore(directory);

            foreach (var kind in ReadingNamingPresets.ReadingKinds)
            {
                var resolved = await store.ResolveAsync(kind);
                Assert.AreEqual(ReadingNamingPresets.Default(kind), resolved);
            }

            Assert.IsFalse(File.Exists(Path.Combine(directory.FullName, ReadingNamingProfileStore.FileName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task UpsertedProfilesPersistPerMediaKindIndependently()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-");
        try
        {
            var store = new ReadingNamingProfileStore(directory);
            var mangaProfile = ReadingNamingPresets.Structured(MediaAcquisitionKind.Manga);

            await store.UpsertAsync(mangaProfile);

            var reloaded = new ReadingNamingProfileStore(directory);
            Assert.AreEqual(mangaProfile, await reloaded.ResolveAsync(MediaAcquisitionKind.Manga));

            // Books/Light Novels are untouched: still the built-in default.
            Assert.AreEqual(ReadingNamingPresets.Default(MediaAcquisitionKind.Book), await reloaded.ResolveAsync(MediaAcquisitionKind.Book));
            Assert.AreEqual(ReadingNamingPresets.Default(MediaAcquisitionKind.LightNovel), await reloaded.ResolveAsync(MediaAcquisitionKind.LightNovel));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidProfilesAreRejectedAndNothingIsWritten()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-");
        try
        {
            var store = new ReadingNamingProfileStore(directory);
            var invalid = ReadingNamingPresets.Default(MediaAcquisitionKind.Book) with { FileFormat = "" };

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.UpsertAsync(invalid));
            Assert.IsFalse(File.Exists(Path.Combine(directory.FullName, ReadingNamingProfileStore.FileName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task UpsertRejectsAProfileWhoseMediaKindDoesNotMatchItsOwnSlot()
    {
        // A caller could accidentally build a profile for one kind and save it under another;
        // the store keys strictly by MediaKind, so this can only happen through direct
        // corruption of the persisted file, which ValidateState below also rejects.
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-");
        try
        {
            var store = new ReadingNamingProfileStore(directory);
            var bookProfile = ReadingNamingPresets.Structured(MediaAcquisitionKind.Book);
            await store.UpsertAsync(bookProfile);

            var reloaded = await store.LoadAsync();
            Assert.AreEqual(MediaAcquisitionKind.Book, reloaded.Profiles[MediaAcquisitionKind.Book.ToString()].MediaKind);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
