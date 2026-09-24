using System.IO.Compression;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Manga;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class MangaUploadTests
{
    [TestMethod]
    public async Task MultipleUploadedArchivesBecomeOneImportableSeries()
    {
        var root = TempDirectory();
        var database = Path.Combine(root, "anilingo.db");
        var uploadRoot = Path.Combine(root, "uploads");
        var cacheRoot = Path.Combine(root, "cache");

        try
        {
            var upload = new MangaUploadService(uploadRoot);
            var sourcePath = await upload.SaveSeriesAsync(
                "Example Series",
                [
                    CreateArchive("Ch. 1.cbz", 1),
                    CreateArchive("Ch. 2.cbz", 1)
                ],
                CancellationToken.None);

            Assert.IsTrue(
                Path.GetFullPath(sourcePath).StartsWith(
                    Path.GetFullPath(uploadRoot),
                    StringComparison.Ordinal));
            Assert.AreEqual(2, Directory.GetFiles(sourcePath, "*.cbz").Length);

            await using var db = await CreateDatabaseAsync(database);
            var repository = new MangaRepository(db);
            var importer = new MangaImportService(repository, cacheRoot);

            var first = await importer.ImportAsync(
                sourcePath,
                CancellationToken.None);
            Assert.AreEqual(2, first.ChapterCount);
            Assert.AreEqual(2, first.PageCount);

            await upload.SaveSeriesAsync(
                "Example Series",
                [CreateArchive("Ch. 1.cbz", 2)],
                CancellationToken.None);

            var refreshed = await importer.ImportAsync(
                sourcePath,
                CancellationToken.None);
            var series = await repository.GetSeriesAsync(
                refreshed.SeriesId,
                CancellationToken.None);

            Assert.IsNotNull(series);
            Assert.AreEqual(2, series.Chapters.Count);
            Assert.AreEqual(
                3,
                series.Chapters.Sum(chapter => chapter.PageCount));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task UploadSanitizesClientPathsInsideManagedRoot()
    {
        var root = TempDirectory();
        var uploadRoot = Path.Combine(root, "uploads");

        try
        {
            var upload = new MangaUploadService(uploadRoot);
            var sourcePath = await upload.SaveSeriesAsync(
                "../Unsafe / Series",
                [CreateArchive("../../outside.cbz", 1)],
                CancellationToken.None);

            var normalizedRoot = Path.GetFullPath(uploadRoot)
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var file = Directory.GetFiles(sourcePath).Single();

            Assert.IsTrue(
                Path.GetFullPath(sourcePath).StartsWith(
                    normalizedRoot,
                    StringComparison.Ordinal));
            Assert.IsTrue(
                Path.GetFullPath(file).StartsWith(
                    normalizedRoot,
                    StringComparison.Ordinal));
            Assert.AreEqual("outside.cbz", Path.GetFileName(file));
            Assert.IsFalse(File.Exists(Path.Combine(root, "outside.cbz")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task CorruptUploadIsRejectedBeforeReplacingManagedSource()
    {
        var root = TempDirectory();
        var uploadRoot = Path.Combine(root, "uploads");

        try
        {
            var upload = new MangaUploadService(uploadRoot);
            var goodSource = await upload.SaveSeriesAsync(
                "Series",
                [CreateArchive("Ch. 1.cbz", 1)],
                CancellationToken.None);
            var destination = Path.Combine(goodSource, "Ch. 1.cbz");
            var before = await File.ReadAllBytesAsync(destination);

            var invalid = new FormFile(
                new MemoryStream([1, 2, 3, 4]),
                0,
                4,
                "archives",
                "Ch. 1.cbz");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => upload.SaveSeriesAsync(
                    "Series",
                    [invalid],
                    CancellationToken.None));

            CollectionAssert.AreEqual(
                before,
                await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task MultipleArchivesRequireSeriesName()
    {
        var root = TempDirectory();
        try
        {
            var upload = new MangaUploadService(Path.Combine(root, "uploads"));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => upload.SaveSeriesAsync(
                    null,
                    [
                        CreateArchive("Ch. 1.cbz", 1),
                        CreateArchive("Ch. 2.cbz", 1)
                    ],
                    CancellationToken.None));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static FormFile CreateArchive(string fileName, int pageCount)
    {
        var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
                   bytes,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            for (var index = 0; index < pageCount; index++)
            {
                var entry = archive.CreateEntry(
                    $"{index + 1:D3}.jpg",
                    CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.WriteByte((byte)(index + 1));
                output.WriteByte(2);
                output.WriteByte(3);
            }
        }

        var data = bytes.ToArray();
        var stream = new MemoryStream(data, writable: false);
        return new FormFile(
            stream,
            0,
            stream.Length,
            "archives",
            fileName);
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

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-manga-upload-{Guid.NewGuid():N}");
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
}
