using System.IO.Compression;
using System.Security.Cryptography;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Manga;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class MangaTests
{
    [TestMethod]
    public async Task CbzImportUsesCanonicalDatabaseAndNeverMutatesSource()
    {
        var root = TempDirectory();
        var database = Path.Combine(root, "anilingo.db");
        var source = Path.Combine(root, "Example.Ch.001.cbz");
        var cache = Path.Combine(root, "cache");

        try
        {
            CreateArchive(source, 3);
            var beforeHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(source)));
            var beforeWrite = File.GetLastWriteTimeUtc(source);

            await using var db = await CreateDatabaseAsync(database);
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository, cache);

            var result = await importer.ImportAsync(
                source,
                CancellationToken.None);

            Assert.AreEqual(1, result.ChapterCount);
            Assert.AreEqual(3, result.PageCount);

            var library = await repository.GetLibraryAsync(
                "reader-a",
                CancellationToken.None);

            Assert.AreEqual(1, library.Count);
            Assert.AreEqual(1, library[0].ChapterCount);
            Assert.IsNotNull(library[0].PreviewChapterId);

            var series = await repository.GetSeriesAsync(
                result.SeriesId,
                CancellationToken.None);
            Assert.IsNotNull(series);
            Assert.AreEqual(1, series.Chapters.Count);
            Assert.AreEqual(3, series.Chapters[0].PageCount);

            var page = await repository.GetPageAsync(
                series.Chapters[0].Id,
                2,
                CancellationToken.None);
            Assert.IsNotNull(page);
            Assert.IsTrue(File.Exists(page.CachedPath));
            Assert.IsTrue(
                Path.GetFullPath(page.CachedPath).StartsWith(
                    Path.GetFullPath(cache),
                    StringComparison.Ordinal));

            var afterHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(source)));
            Assert.AreEqual(beforeHash, afterHash);
            Assert.AreEqual(beforeWrite, File.GetLastWriteTimeUtc(source));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task ProgressAndBookmarksAreProfileScoped()
    {
        var root = TempDirectory();
        var database = Path.Combine(root, "anilingo.db");
        var source = Path.Combine(root, "Series");
        var chapterDirectory = Path.Combine(source, "Vol.1", "Ch. 12");
        var cache = Path.Combine(root, "cache");

        try
        {
            Directory.CreateDirectory(chapterDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(chapterDirectory, "001.jpg"),
                [1, 2, 3]);
            await File.WriteAllBytesAsync(
                Path.Combine(chapterDirectory, "002.jpg"),
                [4, 5, 6]);

            await using var db = await CreateDatabaseAsync(database);
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository, cache);
            var imported = await importer.ImportAsync(
                source,
                CancellationToken.None);
            var series = await repository.GetSeriesAsync(
                imported.SeriesId,
                CancellationToken.None);
            var chapter = await repository.GetChapterAsync(
                series!.Chapters.Single().Id,
                CancellationToken.None);

            Assert.IsNotNull(chapter);
            Assert.AreEqual(12d, chapter.Number);
            Assert.AreEqual(1, chapter.VolumeNumber);

            await repository.SaveProgressAsync(
                "reader-a",
                chapter,
                1,
                CancellationToken.None);
            await repository.SaveProgressAsync(
                "reader-b",
                chapter,
                0,
                CancellationToken.None);

            Assert.AreEqual(
                1,
                (await repository.GetProgressAsync(
                    "reader-a",
                    chapter.SeriesId,
                    CancellationToken.None))!.PageIndex);
            Assert.AreEqual(
                0,
                (await repository.GetProgressAsync(
                    "reader-b",
                    chapter.SeriesId,
                    CancellationToken.None))!.PageIndex);

            var bookmark = await repository.AddBookmarkAsync(
                "reader-a",
                chapter,
                1,
                "Important",
                CancellationToken.None);

            Assert.AreEqual(
                1,
                (await repository.GetBookmarksAsync(
                    "reader-a",
                    chapter.SeriesId,
                    CancellationToken.None)).Count);
            Assert.AreEqual(
                0,
                (await repository.GetBookmarksAsync(
                    "reader-b",
                    chapter.SeriesId,
                    CancellationToken.None)).Count);

            await repository.RemoveBookmarkAsync(
                "reader-b",
                bookmark.Id,
                CancellationToken.None);
            Assert.AreEqual(
                1,
                (await repository.GetBookmarksAsync(
                    "reader-a",
                    chapter.SeriesId,
                    CancellationToken.None)).Count);

            await repository.RemoveBookmarkAsync(
                "reader-a",
                bookmark.Id,
                CancellationToken.None);
            Assert.AreEqual(
                0,
                (await repository.GetBookmarksAsync(
                    "reader-a",
                    chapter.SeriesId,
                    CancellationToken.None)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    [TestMethod]
    public void MangaReaderSupportsPageModesDirectionAndSharedReadingSwitch()
    {
        var root = FindRepositoryRoot();
        var reader = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Manga",
            "Read.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "js",
            "manga-reader.js"));
        var library = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Manga",
            "Index.cshtml"));

        StringAssert.Contains(reader, "data-mode=\"single\"");
        StringAssert.Contains(reader, "data-mode=\"double\"");
        StringAssert.Contains(reader, "data-mode=\"continuous\"");
        StringAssert.Contains(reader, "data-direction-toggle");
        StringAssert.Contains(reader, "data-page-scrubber");
        StringAssert.Contains(script, "direction === \"rtl\"");
        StringAssert.Contains(script, "prefetch");
        StringAssert.Contains(script, "toggleBookmark");
        StringAssert.Contains(library, "href=\"/Novels\"");
        StringAssert.Contains(library, "href=\"/Manga\"");
    }

    [TestMethod]
    public void MangaMigrationStaysInsideAniLingoDatabase()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Data",
            "Migrations",
            "20260924230000_AddMangaReading.cs"));

        StringAssert.Contains(migration, "MangaSeries");
        StringAssert.Contains(migration, "MangaProgress");
        StringAssert.Contains(migration, "MangaBookmarks");
        Assert.IsFalse(
            migration.Contains(
                "Data Source=",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private static void CreateArchive(string path, int pages)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var index = 0; index < pages; index++)
        {
            var entry = archive.CreateEntry(
                $"{index + 1:D3}.jpg",
                CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.WriteByte((byte)(index + 1));
            stream.WriteByte(2);
            stream.WriteByte(3);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-manga-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
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

        throw new DirectoryNotFoundException(
            "Could not locate AniLingo repository root.");
    }
}
