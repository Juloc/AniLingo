using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jularr.Tests;

[TestClass]
public sealed class BookEditionFileTests
{

    [TestMethod]
    public async Task WorkDeleteCascadesEditionAndFileWithoutChangingReaderHierarchy()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = CreateDb(path);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "edition-cascade",
                SourceUrl = "upload://edition.epub",
                Title = "Edition Cascade",
                Format = "EPUB:en"
            };
            db.NovelWorks.Add(work);
            var volume = new NovelVolume
            {
                WorkId = work.Id,
                Number = 1,
                Kind = NovelVolumeKinds.Book,
                SourceKey = "book"
            };
            db.NovelVolumes.Add(volume);

            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = 1,
                SourceUrl = "book://edition-cascade/1",
                Title = "Chapter One",
                OriginalText = "Text",
                SourceHash = "hash"
            };
            db.NovelChapters.Add(chapter);

            var edition = new BookEdition
            {
                WorkId = work.Id,
                EditionKey = "isbn13-9780306406157",
                Language = "en",
                Isbn13 = "9780306406157",
                Title = work.Title,
                IsPrimary = true
            };
            db.BookEditions.Add(edition);

            db.BookFiles.Add(new BookFile
            {
                EditionId = edition.Id,
                FileKey = "sha256-test",
                FileName = "edition.epub",
                Format = "EPUB",
                MediaType = "application/epub+zip",
                SourceKind = "upload",
                SourceUrl = "upload://edition.epub",
                ContentHash = new string('A', 64),
                SizeBytes = 123,
                IsPrimary = true
            });

            await db.SaveChangesAsync();

            Assert.AreEqual(1, await db.NovelChapters.CountAsync());
            Assert.AreEqual(1, await db.BookEditions.CountAsync());
            Assert.AreEqual(1, await db.BookFiles.CountAsync());

            db.NovelWorks.Remove(work);
            await db.SaveChangesAsync();

            Assert.AreEqual(0, await db.NovelChapters.CountAsync());
            Assert.AreEqual(0, await db.BookEditions.CountAsync());
            Assert.AreEqual(0, await db.BookFiles.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task OneWorkCanRepresentMultipleEditionAndFileRecordsWithoutMergingLanguages()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = CreateDb(path);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "multi-edition-test",
                SourceUrl = "upload://en.epub",
                Title = "Shared Story",
                Format = "EPUB:en"
            };
            db.NovelWorks.Add(work);

            var english = new BookEdition
            {
                WorkId = work.Id,
                EditionKey = "isbn13-9780306406157",
                Language = "en",
                Isbn13 = "9780306406157",
                Title = "Shared Story",
                IsPrimary = true
            };
            var indonesian = new BookEdition
            {
                WorkId = work.Id,
                EditionKey = "isbn13-9786020000007",
                Language = "id",
                Isbn13 = "9786020000007",
                Title = "Cerita Bersama",
                IsPrimary = false
            };

            db.BookEditions.AddRange(
                english,
                indonesian);
            await db.SaveChangesAsync();

            var editions = await db.BookEditions
                .AsNoTracking()
                .Where(x => x.WorkId == work.Id)
                .OrderBy(x => x.Language)
                .ToArrayAsync();

            Assert.AreEqual(2, editions.Length);
            Assert.AreEqual(
                1,
                editions.Count(x => x.IsPrimary));
            CollectionAssert.AreEquivalent(
                new[] { "en", "id" },
                editions.Select(x => x.Language).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static AppDbContext CreateDb(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        return new AppDbContext(options);
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"jularr-book-edition-{Guid.NewGuid():N}.db");
}
