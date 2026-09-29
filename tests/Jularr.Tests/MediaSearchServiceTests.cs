using Jularr.Web.Data;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Search;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaSearchServiceTests
{
    [TestMethod]
    public async Task FindsWorksByTitleTypoPrefixAndNativeTitle()
    {
        await using var db = NewContext();
        await DatabaseMigrationBridge.UpgradeAsync(db);

        await SeedNovelAsync(db, "Mushoku Tensei", nativeTitle: "無職転生", key: "n1");
        await SeedNovelAsync(db, "Sword Art Online", nativeTitle: "ソードアート・オンライン", key: "n2");
        await SeedNovelAsync(db, "Overlord", nativeTitle: "オーバーロード", key: "n3");

        var search = new MediaSearchService(db);

        // Exact / strong title match.
        var exact = await search.SearchAsync("Mushoku Tensei");
        Assert.IsTrue(exact.Count >= 1);
        Assert.AreEqual("Mushoku Tensei", exact[0].Title, "Exact title must rank first.");

        // Prefix match.
        var prefix = await search.SearchAsync("Sword");
        Assert.IsTrue(prefix.Any(h => h.Title == "Sword Art Online"), "Prefix search must find the work.");

        // Typo tolerance (fuzzy).
        var typo = await search.SearchAsync("Mushuku Tensai");
        Assert.IsTrue(typo.Any(h => h.Title == "Mushoku Tensei"), "Fuzzy search must tolerate small typos.");

        // Native/localized title is searchable.
        var native = await search.SearchAsync("無職転生");
        Assert.IsTrue(native.Any(h => h.Title == "Mushoku Tensei"), "Native titles must be searchable.");

        // Exact ranks above a weak fuzzy match for the same query.
        var ranked = await search.SearchAsync("Overlord");
        Assert.AreEqual("Overlord", ranked[0].Title);
    }

    [TestMethod]
    public async Task ReturnsEmptyForBlankQueryAndBoundsResults()
    {
        await using var db = NewContext();
        await DatabaseMigrationBridge.UpgradeAsync(db);

        Assert.AreEqual(0, (await new MediaSearchService(db).SearchAsync("   ")).Count);
        Assert.AreEqual(0, (await new MediaSearchService(db).SearchAsync(null)).Count);
    }

    private static async Task SeedNovelAsync(AppDbContext db, string title, string nativeTitle, string key)
    {
        db.NovelWorks.Add(new NovelWork
        {
            Id = Guid.NewGuid(),
            SourceProvider = "test",
            SourceKey = key,
            SourceUrl = $"https://example/{key}",
            Title = title,
            MetadataNativeTitle = nativeTitle,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"jularr-search-{Guid.NewGuid():N}.db")}")
            .Options);
}
