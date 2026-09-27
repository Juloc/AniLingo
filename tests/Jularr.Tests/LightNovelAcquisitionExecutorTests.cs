using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.ReadingAcquisition;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>Review #485 items 3 and 4 for Light Novel requests.</summary>
[TestClass]
public sealed class LightNovelAcquisitionExecutorTests
{
    [TestMethod]
    public async Task ApprovedSyosetuRequestIsImportedDirectlyWithoutUsenet()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-ln-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using (var db = new AppDbContext(
                             new DbContextOptionsBuilder<AppDbContext>()
                                 .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                                 .Options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(db);
                var source = new FakeSyosetu();
                // A null engine proves the Usenet path is never touched.
                var executor = new LightNovelAcquisitionRequestExecutor(
                    null!,
                    null!,
                    new NovelImportService(db, [source]));

                var result = await executor.ExecuteAsync(
                    Request(NcodeNovelSourceProvider.ProviderKey, "n9669bk", payloadJson: null),
                    CancellationToken.None);

                Assert.AreEqual(AcquisitionRequestStatus.Completed, result.Status, result.Message);
                Assert.AreEqual("https://ncode.syosetu.com/n9669bk/", source.LastUrl);
                var work = await db.NovelWorks.SingleAsync();
                Assert.AreEqual($"/Novels/Work/{work.Id}", result.ResultUrl);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SyosetuImportFailureFailsTheRequestWithTheReason()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-ln-exec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using (var db = new AppDbContext(
                             new DbContextOptionsBuilder<AppDbContext>()
                                 .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                                 .Options))
            {
                await DatabaseMigrationBridge.UpgradeAsync(db);
                var executor = new LightNovelAcquisitionRequestExecutor(
                    null!,
                    null!,
                    new NovelImportService(db, [new FakeSyosetu(fail: true)]));

                var result = await executor.ExecuteAsync(
                    Request(NcodeNovelSourceProvider.ProviderKey, "n9669bk", payloadJson: null),
                    CancellationToken.None);

                Assert.AreEqual(AcquisitionRequestStatus.Failed, result.Status);
                StringAssert.Contains(result.Message, "No chapters");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void DraftPayloadKeepsNativeTitleAsAliasAndAuthorAsAuthor()
    {
        var json = ReadingAcquisitionEngine.LightNovelDraftPayload(
            "Mushoku Tensei",
            "無職転生",
            "Rifujin na Magonote");
        var request = Request("anilist", "85470", json, subtitle: "Rifujin na Magonote");

        var payload = ReadingAcquisitionEngine.ReadPayload(
            request,
            new ReadingAcquisitionTarget(MediaAcquisitionKind.LightNovel, request.Title, [], request.Subtitle));
        var queries = ReadingUsenetSearch.Queries(
            ReadingAcquisitionEngine.ToTarget(MediaAcquisitionKind.LightNovel, payload));

        Assert.AreEqual("Rifujin na Magonote", payload.Author);
        CollectionAssert.AreEqual(new[] { "無職転生" }, payload.Aliases!.ToArray());
        CollectionAssert.Contains(queries.ToList(), "Rifujin na Magonote Mushoku Tensei");
        CollectionAssert.Contains(queries.ToList(), "無職転生");
        CollectionAssert.DoesNotContain(queries.ToList(), "無職転生 Mushoku Tensei");
    }

    [TestMethod]
    public void DraftPayloadWithoutAuthorHasNoAuthor()
    {
        var payload = JsonSerializer.Deserialize<ReadingRequestPayload>(
            ReadingAcquisitionEngine.LightNovelDraftPayload("Mushoku Tensei", "無職転生", null),
            JsonSerializerOptions.Web)!;

        Assert.IsNull(payload.Author);
        CollectionAssert.AreEqual(new[] { "無職転生" }, payload.Aliases!.ToArray());
    }

    private static AcquisitionRequest Request(
        string provider,
        string externalId,
        string? payloadJson,
        string? subtitle = null) =>
        new(
            Guid.NewGuid(),
            MediaAcquisitionKind.LightNovel,
            provider,
            externalId,
            "Mushoku Tensei",
            subtitle,
            null,
            payloadJson,
            "owner",
            AcquisitionRequestStatus.Searching,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "owner",
            DateTime.UtcNow);

    private sealed class FakeSyosetu(bool fail = false) : INovelSourceProvider
    {
        public string Key => NcodeNovelSourceProvider.ProviderKey;
        public string? LastUrl { get; private set; }

        public bool CanHandle(Uri sourceUri) => sourceUri.Host == "ncode.syosetu.com";

        public Task<NovelSourceWorkSnapshot> GetWorkAsync(Uri sourceUri, CancellationToken cancellationToken)
        {
            LastUrl = sourceUri.ToString();
            if (fail)
            {
                throw new InvalidOperationException("No chapters were found on the Narou work page.");
            }

            return Task.FromResult(new NovelSourceWorkSnapshot(
                Key,
                "n9669bk",
                sourceUri.ToString(),
                "無職転生",
                "理不尽な孫の手",
                null,
                [new NovelSourceChapterReference(1, "One", "https://ncode.syosetu.com/n9669bk/1/")]));
        }

        public Task<NovelSourceChapterSnapshot> GetChapterAsync(Uri sourceUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not used.");
    }
}
