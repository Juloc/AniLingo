using AniLingo.Web.Data;
using AniLingo.Web.Data.Migrations;
using AniLingo.Web.Features.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Tests;

[TestClass]
public sealed class MetadataTests
{
    [TestMethod]
    public void PreferredTitleUsesEnglishThenRomajiThenNative()
    {
        Assert.AreEqual(
            "Frieren: Beyond Journey's End",
            AnimeMetadataTitles.Choose(
                "Frieren: Beyond Journey's End",
                "Sousou no Frieren",
                "葬送のフリーレン",
                "Local"));

        Assert.AreEqual(
            "Sousou no Frieren",
            AnimeMetadataTitles.Choose(
                null,
                "Sousou no Frieren",
                "葬送のフリーレン",
                "Local"));

        Assert.AreEqual(
            "Local",
            AnimeMetadataTitles.Choose(null, null, null, "Local"));
    }

    [TestMethod]
    public void AniListSearchResponseMapsCachedDisplayFieldsAndSkipsAdultMedia()
    {
        const string json = """
        {
          "data": {
            "Page": {
              "media": [
                {
                  "id": 154587,
                  "title": {
                    "romaji": "Sousou no Frieren",
                    "english": "Frieren: Beyond Journey's End",
                    "native": "葬送のフリーレン"
                  },
                  "description": "An elf mage returns after an adventure.",
                  "coverImage": {
                    "extraLarge": "https://img.example/frieren-large.jpg",
                    "large": "https://img.example/frieren.jpg"
                  },
                  "bannerImage": "https://img.example/frieren-banner.jpg",
                  "format": "TV",
                  "status": "FINISHED",
                  "season": "FALL",
                  "seasonYear": 2023,
                  "episodes": 28,
                  "duration": 24,
                  "isAdult": false
                },
                {
                  "id": 999999,
                  "title": { "romaji": "Hidden" },
                  "isAdult": true
                }
              ]
            }
          }
        }
        """;

        var results = AniListMetadataProvider.ParseSearchResponse(json);

        Assert.AreEqual(1, results.Count);
        var result = results[0];
        Assert.AreEqual("154587", result.ExternalId);
        Assert.AreEqual("Frieren: Beyond Journey's End", result.PreferredTitle);
        Assert.AreEqual("葬送のフリーレン", result.NativeTitle);
        Assert.AreEqual(28, result.EpisodeCount);
        Assert.AreEqual(24, result.EpisodeDurationMinutes);
        Assert.AreEqual("https://img.example/frieren.jpg", result.CoverImageUrl);
    }


    [TestMethod]
    public void MigrationSnapshotMatchesRuntimeModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new AppDbContext(options);
        var differ = db.GetService<IMigrationsModelDiffer>();
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var snapshotModel = initializer.Initialize(
            new AppDbContextModelSnapshot().Model,
            designTime: true);

        var operations = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            runtimeModel.GetRelationalModel());

        Assert.AreEqual(
            0,
            operations.Count,
            string.Join(", ", operations.Select(operation => operation.GetType().Name)));
    }

    [TestMethod]
    public async Task FreshDatabaseAppliesMigrationBaselineAndMetadataMigration()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-metadata-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();

            CollectionAssert.Contains(
                applied,
                DatabaseMigrationBridge.Epoch2BaselineMigration);
            CollectionAssert.Contains(
                applied,
                "20260922153100_AddAnimeMetadata");
            Assert.AreEqual(0, await db.AnimeMetadata.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task Epoch2DatabaseIsStampedThenUpgradedInPlace()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-epoch2-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Anime (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE Episodes (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE EpisodeTerms (EpisodeId TEXT NOT NULL, TermId TEXT NOT NULL, PRIMARY KEY (EpisodeId, TermId));
                    CREATE TABLE LibraryRoots (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE MediaFiles (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE Reviews (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ProfileId TEXT NOT NULL DEFAULT 'default'
                    );
                    CREATE TABLE SubtitleCues (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
                    CREATE TABLE SubtitleTracks (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE Terms (Id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE UserTerms (
                        Id TEXT NOT NULL PRIMARY KEY,
                        ProfileId TEXT NOT NULL DEFAULT 'default'
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            CollectionAssert.Contains(
                applied,
                DatabaseMigrationBridge.Epoch2BaselineMigration);
            CollectionAssert.Contains(
                applied,
                "20260922153100_AddAnimeMetadata");
            CollectionAssert.Contains(
                applied,
                "20260923110000_AddOfflineReviewEventIds");

            await using var verification = new SqliteConnection($"Data Source={databasePath}");
            await verification.OpenAsync();
            await using var check = verification.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AnimeMetadata';";
            Assert.AreEqual(1L, Convert.ToInt64(await check.ExecuteScalarAsync()));

            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Reviews_ProfileId_ClientEventId';";
            Assert.AreEqual(1L, Convert.ToInt64(await check.ExecuteScalarAsync()));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
