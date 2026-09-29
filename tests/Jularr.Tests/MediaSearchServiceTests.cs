using Jularr.Web.Features.Search;

namespace Jularr.Tests;

/// <summary>The PostgreSQL full-text + fuzzy matching of the shared search backend (#570).</summary>
[TestClass]
public sealed class MediaSearchServiceTests
{
    [TestMethod]
    public async Task FindsWorksByTitleTypoPrefixAndNativeTitle()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddNovelAsync("Mushoku Tensei", nativeTitle: "無職転生");
        await fixture.AddNovelAsync("Sword Art Online", nativeTitle: "ソードアート・オンライン");
        await fixture.AddNovelAsync("Overlord", nativeTitle: "オーバーロード");

        // Exact / strong title match.
        var exact = await fixture.SearchAsync("Mushoku Tensei");
        Assert.IsTrue(exact.Items.Count >= 1);
        Assert.AreEqual("Mushoku Tensei", exact.Items[0].Title, "Exact title must rank first.");

        // Prefix match.
        var prefix = await fixture.SearchAsync("Sword");
        Assert.IsTrue(prefix.Items.Any(h => h.Title == "Sword Art Online"), "Prefix search must find the work.");

        // Typo tolerance (fuzzy).
        var typo = await fixture.SearchAsync("Mushuku Tensai");
        Assert.IsTrue(typo.Items.Any(h => h.Title == "Mushoku Tensei"), "Fuzzy search must tolerate small typos.");

        // Native/localized title is searchable.
        var native = await fixture.SearchAsync("無職転生");
        Assert.IsTrue(native.Items.Any(h => h.Title == "Mushoku Tensei"), "Native titles must be searchable.");

        // Exact ranks above a weak fuzzy match for the same query.
        var ranked = await fixture.SearchAsync("Overlord");
        Assert.AreEqual("Overlord", ranked.Items[0].Title);
    }

    [TestMethod]
    public async Task ReturnsEmptyForBlankQueryAndBoundsResults()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddNovelAsync("Anything");

        Assert.AreEqual(0, (await fixture.SearchAsync("   ")).Items.Count);
        Assert.AreEqual(0, (await fixture.SearchAsync(null!)).Items.Count);

        for (var index = 0; index < 12; index++)
        {
            await fixture.AddNovelAsync($"Bounded Saga {index:D2}");
        }

        var page = await fixture.SearchAsync("Bounded", limit: 5);
        Assert.AreEqual(5, page.Items.Count, "A page never exceeds the requested limit.");
        Assert.AreEqual(12, page.Total);
        Assert.IsTrue(page.HasMore);

        var oversized = await fixture.SearchAsync("Bounded", limit: 100_000);
        Assert.AreEqual(MediaSearchService.MaxLimit, oversized.Limit, "The limit is clamped to the service maximum.");
    }

    [TestMethod]
    public async Task OverlongQueriesAreCutInsteadOfRejectedOrScanned()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddNovelAsync("Cut Query Saga");

        // Punctuation is ignored by full-text search, so this only finds the title if the request is served at all.
        var query = "Cut Query Saga " + new string('!', MediaSearchService.MaxQueryLength * 50);
        var page = await fixture.SearchAsync(query);

        Assert.AreEqual("Cut Query Saga", page.Items.Single().Title);
        Assert.AreEqual(0, (await fixture.SearchAsync(new string('y', 10_000))).Items.Count);
    }

    [TestMethod]
    public async Task TypedWildcardCharactersAreLiteralText()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddNovelAsync("100% Pure");
        await fixture.AddNovelAsync("Pure Imagination");
        await fixture.AddNovelAsync("Wild_Card Story");
        await fixture.AddNovelAsync("Wildcard Story");

        var percent = await fixture.SearchAsync("100%");
        CollectionAssert.AreEqual(new[] { "Novel:100% Pure" }, GlobalSearchFixture.Rows(percent));

        var underscore = await fixture.SearchAsync("wild_card");
        Assert.AreEqual("Wild_Card Story", underscore.Items[0].Title, "An underscore is a character, not a one-character wildcard.");
    }
}
