using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Sonarr;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

[TestClass]
public sealed class LibraryReconciliationTests
{
    [TestMethod]
    public async Task ReconciliationRemovesMissingMediaAndOrphanEpisodeButPreservesAnime()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var anime = new Anime
        {
            Key = "frieren",
            Title = "Frieren"
        };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = 1,
            Number = 1,
            Title = "Episode 1"
        };
        fixture.Db.Anime.Add(anime);
        fixture.Db.Episodes.Add(episode);
        fixture.Db.MediaFiles.Add(new MediaFile
        {
            LibraryRootId = fixture.Root.Id,
            EpisodeId = episode.Id,
            Path = Path.Combine(fixture.Root.Path, "Frieren", "Season 01", "missing.mkv"),
            SizeBytes = 123,
            LastWriteTimeUtc = DateTime.UtcNow.AddHours(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Scanner.ScanAsync(
            fixture.Root.Id,
            CancellationToken.None);

        Assert.AreEqual(1, result.Removed);
        Assert.AreEqual(0, await fixture.Db.MediaFiles.CountAsync());
        Assert.AreEqual(0, await fixture.Db.Episodes.CountAsync());
        Assert.AreEqual(1, await fixture.Db.Anime.CountAsync());
    }

    [TestMethod]
    public async Task UnavailableRootNeverDeletesExistingLibraryState()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var anime = new Anime
        {
            Key = "frieren",
            Title = "Frieren"
        };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = 1,
            Number = 1,
            Title = "Episode 1"
        };
        fixture.Db.Anime.Add(anime);
        fixture.Db.Episodes.Add(episode);
        fixture.Db.MediaFiles.Add(new MediaFile
        {
            LibraryRootId = fixture.Root.Id,
            EpisodeId = episode.Id,
            Path = Path.Combine(fixture.Root.Path, "Frieren", "Season 01", "episode.mkv"),
            SizeBytes = 123,
            LastWriteTimeUtc = DateTime.UtcNow.AddHours(-1)
        });
        await fixture.Db.SaveChangesAsync();

        Directory.Delete(fixture.Root.Path, recursive: true);

        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(
            () => fixture.Scanner.ScanAsync(
                fixture.Root.Id,
                CancellationToken.None));

        Assert.AreEqual(1, await fixture.Db.MediaFiles.CountAsync());
        Assert.AreEqual(1, await fixture.Db.Episodes.CountAsync());
        Assert.AreEqual(1, await fixture.Db.Anime.CountAsync());

        var root = await fixture.Db.LibraryRoots
            .AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Root.Id);
        Assert.IsNull(root.LastScannedAt);
    }

    private sealed class ReconciliationFixture : IAsyncDisposable
    {
        private ReconciliationFixture(
            string tempRoot,
            AppDbContext db,
            LibraryRoot root,
            LibraryScanner scanner)
        {
            TempRoot = tempRoot;
            Db = db;
            Root = root;
            Scanner = scanner;
        }

        public string TempRoot { get; }
        public AppDbContext Db { get; }
        public LibraryRoot Root { get; }
        public LibraryScanner Scanner { get; }

        public static async Task<ReconciliationFixture> CreateAsync()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-reconcile-{Guid.NewGuid():N}");
            var libraryPath = Path.Combine(tempRoot, "anime");
            var dictionaryPath = Path.Combine(tempRoot, "dictionary");
            var databasePath = Path.Combine(tempRoot, "anilingo.db");

            Directory.CreateDirectory(libraryPath);
            Directory.CreateDirectory(dictionaryPath);
            await File.WriteAllTextAsync(
                Path.Combine(dictionaryPath, "jmdict-ger.tsv"),
                "");
            await File.WriteAllTextAsync(
                Path.Combine(dictionaryPath, "jmdict-eng-common.tsv"),
                "");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var root = new LibraryRoot
            {
                Name = "Anime",
                Path = libraryPath
            };
            db.LibraryRoots.Add(root);
            await db.SaveChangesAsync();

            var vocabulary = new VocabularyService(
                db,
                new JapaneseTermExtractor(new EmptyMorphology()),
                new JapaneseDictionary(dictionaryPath));
            var subtitleImport = new SubtitleImportService(db, vocabulary);
            var embedded = new EmbeddedSubtitleExtractor(
                new MediaProcessRunner(NullLogger<MediaProcessRunner>.Instance),
                NullLogger<EmbeddedSubtitleExtractor>.Instance);
            var sonarrStore = new SonarrConnectionStore(
                DataProtectionProvider.Create(
                    new DirectoryInfo(Path.Combine(tempRoot, "keys"))));
            var sonarrImport = new SonarrArtworkImportService(
                db,
                new TestHttpClientFactory(),
                NullLogger<SonarrArtworkImportService>.Instance);
            var sonarrSync = new SonarrArtworkSyncService(
                sonarrStore,
                sonarrImport,
                NullLogger<SonarrArtworkSyncService>.Instance);
            var scanner = new LibraryScanner(
                db,
                subtitleImport,
                embedded,
                sonarrSync,
                NullLogger<LibraryScanner>.Instance);

            return new ReconciliationFixture(tempRoot, db, root, scanner);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class EmptyMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }
}
