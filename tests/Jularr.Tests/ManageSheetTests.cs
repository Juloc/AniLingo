namespace Jularr.Tests;

/// <summary>
/// #519 (part of epic #510): every owner-only control on Library/Anime moves into one shared
/// owner-only Manage sheet. A normal user's rendered page must not contain any of the moved
/// handlers' forms or links; the owner's rendered page must contain them, inside the sheet.
/// </summary>
[TestClass]
public sealed class ManageSheetTests
{
    // Handlers moved off the consumer Anime page and into its Manage sheet (#519). Render only
    // once the anime has a provider match (RefreshMetadata/RemoveMetadata); MatchMetadata and
    // MatchEpisodeRange only render for search results, which is exercised on the owner side.
    private static readonly string[] MovedAnimeHandlers =
    [
        "handler=RefreshMetadata",
        "handler=RemoveMetadata"
    ];

    [TestMethod]
    public async Task NormalUserAnimePageHasNoOwnerHandlersOrManageSheetAsync()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var anime = await host.AddAnimeAsync("Owner-Only Sample");
        await host.AddEpisodeAsync(anime, season: 1, number: 1);
        await host.AddAnimeMetadataMatchAsync(anime);

        var html = await host.GetHtmlAsync($"/Library/Anime/{anime.Id}", asOwner: false);

        foreach (var handler in MovedAnimeHandlers)
        {
            Assert.IsFalse(
                html.Contains(handler, StringComparison.Ordinal),
                $"A normal user must never receive the owner-only {handler} form.");
        }

        Assert.IsFalse(
            html.Contains("id=\"anime-manage\"", StringComparison.Ordinal),
            "A normal user must not receive the owner-only Manage sheet dialog at all.");
        Assert.IsFalse(html.Contains("data-manage-sheet-open", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("/Library/Rename/", StringComparison.Ordinal));
        Assert.IsFalse(html.Contains("/Library/AnimeRepair/", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OwnerAnimePageHasEveryMovedHandlerInsideTheManageSheetAsync()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var anime = await host.AddAnimeAsync("Owner-Only Sample");
        await host.AddEpisodeAsync(anime, season: 1, number: 1);
        await host.AddAnimeMetadataMatchAsync(anime);

        var html = await host.GetHtmlAsync($"/Library/Anime/{anime.Id}", asOwner: true);

        // The trigger and the dialog it opens are both present, and the dialog carries every
        // moved handler that is present once the anime has a provider match.
        StringAssert.Contains(html, "data-manage-sheet-open=\"anime-manage\"");
        StringAssert.Contains(html, "id=\"anime-manage\"");
        foreach (var handler in MovedAnimeHandlers)
        {
            StringAssert.Contains(html, handler, $"The owner's Manage sheet must still offer {handler}.");
        }

        StringAssert.Contains(html, "/Library/Rename/" + anime.Id);
        StringAssert.Contains(html, "/Library/AnimeRepair/" + anime.Id);

        // The trigger/dialog markup must come from the shared partial (manage-sheet.css classes),
        // not a page-specific reimplementation.
        StringAssert.Contains(html, "manage-sheet-trigger");
        StringAssert.Contains(html, "manage-sheet-dialog");
    }

    [TestMethod]
    public async Task ConsumerLayoutNeverMentionsMappingOrProviderDiagnosticsAsync()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var anime = await host.AddAnimeAsync("Owner-Only Sample");
        await host.AddEpisodeAsync(anime, season: 1, number: 1);

        var normalUserHtml = await host.GetHtmlAsync($"/Library/Anime/{anime.Id}", asOwner: false);

        // Core product rule (#510): a normal user must not see mapping state, provider ids or
        // file paths anywhere on the consumer page.
        Assert.IsFalse(normalUserHtml.Contains("Mapping needs review", StringComparison.Ordinal));
        Assert.IsFalse(normalUserHtml.Contains("needs review", StringComparison.OrdinalIgnoreCase));
    }
}
