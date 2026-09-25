using AniLingo.Web.Features.Books;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookTranslationMemoryEditorTests
{
    [TestMethod]
    public async Task OwnerStyleMutationsPersistAndExplicitTermEditCanReplaceLockedChoice()
    {
        var root = TempDirectory();

        try
        {
            var store = new BookTranslationMemoryStore(root);
            var workId = Guid.NewGuid();

            var bible = await store.GetOrCreateAsync(
                workId,
                "en",
                "id",
                _ => Task.FromResult(
                    new BookTranslationBibleSeed(
                        "Third person",
                        "Adventure prose",
                        "Neutral",
                        "General",
                        ["Fantasy"],
                        [],
                        [
                            new BookTranslationTerm(
                                "Mana Core",
                                "Inti Mana",
                                "magic",
                                "Initial AI choice",
                                Locked: true)
                        ])),
                CancellationToken.None);

            Assert.IsNotNull(bible);

            await store.UpdateOverviewAsync(
                workId,
                "id",
                "First-person retrospective",
                "Quiet literary fantasy",
                "Informal but restrained",
                "Adult",
                CancellationToken.None);

            await store.UpsertTermAsync(
                workId,
                "id",
                "Mana Core",
                "Pusat Mana",
                "magic",
                "Explicit owner correction",
                locked: true,
                CancellationToken.None);

            var reloaded = await store.LoadAsync(
                workId,
                "id",
                CancellationToken.None);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(
                "First-person retrospective",
                reloaded.NarrativePerspective);
            Assert.AreEqual(
                "Quiet literary fantasy",
                reloaded.OverallStyle);

            var term = reloaded.Terms.Single(x =>
                x.Source == "Mana Core");

            Assert.AreEqual("Pusat Mana", term.Target);
            Assert.AreEqual(
                "Explicit owner correction",
                term.Notes);
            Assert.IsTrue(term.Locked);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task EntityAndTermCanBeAddedRemovedAndMemoryCanBeReset()
    {
        var root = TempDirectory();

        try
        {
            var store = new BookTranslationMemoryStore(root);
            var workId = Guid.NewGuid();

            await store.GetOrCreateAsync(
                workId,
                "en",
                "id",
                _ => Task.FromResult(
                    BookTranslationBibleSeed.Empty),
                CancellationToken.None);

            await store.UpsertEntityAsync(
                workId,
                "id",
                "The Headmaster",
                "Kepala Akademi",
                "character",
                "Academy leader",
                "he/him",
                "Alice distrusts him",
                "Formal and measured",
                CancellationToken.None);

            await store.UpsertTermAsync(
                workId,
                "id",
                "Silver Gate",
                "Gerbang Perak",
                "place",
                null,
                locked: true,
                CancellationToken.None);

            var populated = await store.LoadAsync(
                workId,
                "id",
                CancellationToken.None);

            Assert.IsNotNull(populated);
            Assert.AreEqual(1, populated.Entities.Count);
            Assert.AreEqual(1, populated.Terms.Count);

            await store.RemoveEntityAsync(
                workId,
                "id",
                "The Headmaster",
                "character",
                CancellationToken.None);
            await store.RemoveTermAsync(
                workId,
                "id",
                "Silver Gate",
                CancellationToken.None);

            var emptied = await store.LoadAsync(
                workId,
                "id",
                CancellationToken.None);

            Assert.IsNotNull(emptied);
            Assert.AreEqual(0, emptied.Entities.Count);
            Assert.AreEqual(0, emptied.Terms.Count);

            await store.ResetAsync(
                workId,
                "id",
                CancellationToken.None);

            Assert.IsNull(
                await store.LoadAsync(
                    workId,
                    "id",
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task AiMemoryMergeStillCannotOverrideOwnerLockedGlossaryTerm()
    {
        var root = TempDirectory();

        try
        {
            var store = new BookTranslationMemoryStore(root);
            var workId = Guid.NewGuid();

            var bible = await store.GetOrCreateAsync(
                workId,
                "en",
                "id",
                _ => Task.FromResult(
                    BookTranslationBibleSeed.Empty),
                CancellationToken.None);

            await store.UpsertTermAsync(
                workId,
                "id",
                "Silver Gate",
                "Gerbang Perak",
                "place",
                "Owner choice",
                locked: true,
                CancellationToken.None);

            bible = (await store.LoadAsync(
                workId,
                "id",
                CancellationToken.None))!;

            await store.ApplyChapterDeltaAsync(
                bible,
                Guid.NewGuid(),
                1,
                "Opening",
                new BookTranslationMemoryDelta(
                    "The gate opens.",
                    null,
                    [],
                    [
                        new BookTranslationTerm(
                            "Silver Gate",
                            "Pintu Perak",
                            "place",
                            "AI later suggestion",
                            Locked: false)
                    ]),
                CancellationToken.None);

            var reloaded = await store.LoadAsync(
                workId,
                "id",
                CancellationToken.None);

            Assert.IsNotNull(reloaded);
            var term = reloaded.Terms.Single();
            Assert.AreEqual("Gerbang Perak", term.Target);
            Assert.IsTrue(term.Locked);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "anilingo-book-bible-editor",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }
}
