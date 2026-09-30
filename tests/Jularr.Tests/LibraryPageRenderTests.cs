using System.Net;
using System.Text.RegularExpressions;
using Jularr.Web.Features.Collections;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Progress;

namespace Jularr.Tests;

/// <summary>
/// The Library page rendered end to end (docs/mockups/library): the Library | Collections switch, one
/// toolbar with the filter count, sort and layout, poster cards with a language line, and every body state.
/// </summary>
[TestClass]
public sealed class LibraryPageRenderTests
{
    private const string Profile = "test-profile";
    private const string Cover = "https://img.example/starfall.jpg";

    private static async Task<(ManageSheetPageTestHost Host, Anime Starfall, Anime Amber)> SeedAsync()
    {
        var host = await ManageSheetPageTestHost.CreateAsync();
        var starfall = await host.AddAnimeAsync("Starfall Chronicle");
        host.Db.Add(new AnimeMetadata
        {
            AnimeId = starfall.Id,
            Provider = "anilist",
            ExternalId = "9001",
            PreferredTitle = "Starfall Chronicle",
            CoverImageUrl = Cover,
            Format = "TV",
            Status = "RELEASING",
            SeasonYear = 2024,
            EpisodeCount = 12,
            AverageScore = 82
        });

        var first = await host.AddEpisodeAsync(starfall, season: 1, number: 1);
        var second = await host.AddEpisodeAsync(starfall, season: 1, number: 2);
        await host.AddEpisodeAsync(starfall, season: 1, number: 3);

        var root = new LibraryRoot { Name = "Media", Path = $"/media/{Guid.NewGuid():N}" };
        host.Db.Add(root);
        foreach (var episode in new[] { first, second })
        {
            var file = new MediaFile { LibraryRootId = root.Id, EpisodeId = episode.Id, Path = $"{root.Path}/{episode.Number}.mkv", SizeBytes = 1 };
            host.Db.Add(file);
            host.Db.Add(new MediaAnalysis
            {
                MediaFileId = file.Id,
                Status = MediaAnalysisStatus.Succeeded,
                DurationSeconds = 1440,
                SourceLastWriteTimeUtc = DateTime.UtcNow
            });
            host.Db.Add(new MediaAnalysisStream { MediaFileId = file.Id, StreamIndex = 1, Kind = MediaStreamKind.Audio, Language = "jpn" });
            host.Db.Add(new MediaAnalysisStream { MediaFileId = file.Id, StreamIndex = 2, Kind = MediaStreamKind.Audio, Language = "ger" });
            host.Db.Add(new MediaAnalysisStream { MediaFileId = file.Id, StreamIndex = 3, Kind = MediaStreamKind.Subtitle, Language = "eng" });
        }

        host.Db.Add(new EpisodeProgress { ProfileId = Profile, EpisodeId = first.Id, IsCompleted = true, UpdatedAt = DateTime.UtcNow });

        var amber = await host.AddAnimeAsync("Amber Nights");
        await host.AddEpisodeAsync(amber, season: 1, number: 1);
        await host.Db.SaveChangesAsync();
        return (host, starfall, amber);
    }

    [TestMethod]
    public async Task LibraryShowsTheSwitchToolbarAndPosterCardsWithLanguagesAndAvailability()
    {
        var (host, starfall, _) = await SeedAsync();
        await using var _host = host;
        host.Db.Add(new ProfilePlaybackPreferences { ProfileId = Profile, PreferredAudioLanguage = "de" });
        await host.Db.SaveChangesAsync();

        var html = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library", asOwner: false));

        var head = Between(html, "<header class=\"lib-head\"", "</header>");
        StringAssert.Contains(head, "Library");
        StringAssert.Contains(head, "Collections");
        StringAssert.Contains(head, "lib-switch-item is-active\" href=\"/Library\"");
        StringAssert.Contains(head, "href=\"/Library?section=collections\"");
        Assert.IsFalse(head.Contains("Import from Sonarr", StringComparison.Ordinal), "Owner tools are owner-only.");
        StringAssert.Contains(html, "library-type-tabs");
        StringAssert.Contains(html, "2 items");

        var toolbar = Between(html, "<div class=\"lib-toolbar\"", "</nav>");
        StringAssert.Contains(toolbar, "Filters");
        StringAssert.Contains(toolbar, "Sort: Recently added");
        StringAssert.Contains(toolbar, "href=\"/Library?view=list\"");
        Assert.IsFalse(toolbar.Contains("lib-badge", StringComparison.Ordinal), "No filter is active.");

        var starfallCard = Between(html, "<article class=\"lib-card lib-card-partial\"", "</article>");
        StringAssert.Contains(starfallCard, Cover);
        StringAssert.Contains(starfallCard, $"/Library/Anime/{starfall.Id}");
        StringAssert.Contains(starfallCard, "Starfall Chronicle");
        StringAssert.Contains(starfallCard, "Episode 2");
        StringAssert.Contains(starfallCard, "lib-card-bar");
        StringAssert.Contains(starfallCard, "Continue watching");
        StringAssert.Contains(starfallCard, "lib-lang is-preferred\">DE<");
        StringAssert.Contains(starfallCard, ">JA<");
        StringAssert.Contains(starfallCard, ">EN<");
        StringAssert.Contains(starfallCard, "Partly available");

        var amberCard = Between(html, "<article class=\"lib-card lib-card-missing\"", "</article>");
        StringAssert.Contains(amberCard, "Amber Nights");
        StringAssert.Contains(amberCard, "lib-card-initial");
        StringAssert.Contains(amberCard, "Not available");
    }

    [TestMethod]
    public async Task ActiveFiltersAreCountedOnTheButtonKeptInTheAddressAndNarrowTheGrid()
    {
        var (host, _, _) = await SeedAsync();
        await using var _host = host;

        var html = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library?progress=inprogress&year=2024&sort=title", asOwner: false));

        StringAssert.Contains(html, "1 of 2 items");
        StringAssert.Contains(html, "<span class=\"lib-badge\">2</span>");
        StringAssert.Contains(html, "Sort: Title (A-Z)");
        StringAssert.Contains(html, "Starfall Chronicle");
        Assert.IsFalse(html.Contains("Amber Nights", StringComparison.Ordinal));

        var panel = Between(html, "<div class=\"lib-pop-panel lib-filter-panel\"", "</form>");
        StringAssert.Contains(panel, "name=\"sort\" value=\"title\"");
        StringAssert.Contains(panel, "name=\"progress\" value=\"inprogress\" checked");
        StringAssert.Contains(panel, "Reset all");
        StringAssert.Contains(panel, "href=\"/Library?sort=title\"");
        StringAssert.Contains(panel, "<option value=\"2024\" selected");
        StringAssert.Contains(panel, "Audio language");
        StringAssert.Contains(panel, "Partially available");
        Assert.IsFalse(panel.Contains("Fully available", StringComparison.Ordinal), "An option nothing matches is not offered.");
        Assert.IsFalse(panel.Contains("My preferred language", StringComparison.Ordinal), "Without a preference the option is not offered.");
    }

    [TestMethod]
    public async Task FiltersThatMatchNothingOfferAResetAndTheListLayoutIsTheSameCardAsARow()
    {
        var (host, _, _) = await SeedAsync();
        await using var _host = host;

        var none = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library?progress=completed&view=list", asOwner: false));
        StringAssert.Contains(none, "No titles match these filters");
        StringAssert.Contains(none, "Reset filters");
        StringAssert.Contains(none, "href=\"/Library?view=list\"");
        Assert.IsFalse(none.Contains("<article class=\"lib-card", StringComparison.Ordinal));

        var list = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library?view=list", asOwner: false));
        StringAssert.Contains(list, "class=\"lib-grid lib-list\"");
        StringAssert.Contains(list, "lib-icon-btn is-active\" href=\"/Library?view=list\"");
    }

    [TestMethod]
    public async Task EmptyLibraryExplainsTheNextStepForTheOwnerAndStaysQuietForOthers()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();

        var owner = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library", asOwner: true));
        StringAssert.Contains(owner, "Library is empty");
        StringAssert.Contains(owner, "Add a root and run the first scan.");
        StringAssert.Contains(owner, "Import from Sonarr");
        Assert.IsFalse(owner.Contains("lib-toolbar", StringComparison.Ordinal), "Nothing to filter yet.");

        var member = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library", asOwner: false));
        StringAssert.Contains(member, "Nothing is in the library yet.");
        Assert.IsFalse(member.Contains("Add a root", StringComparison.Ordinal));
        Assert.IsFalse(member.Contains("Import from Sonarr", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CollectionsViewShowsMosaicTilesWithCountAndKindInsteadOfTheMediaTabs()
    {
        var (host, starfall, _) = await SeedAsync();
        await using var _host = host;
        var work = new Work { MediaType = WorkMediaType.Anime, CanonicalTitle = "Starfall Chronicle" };
        host.Db.Add(work);
        host.Db.Add(new WorkSourceLink { WorkId = work.Id, SourceKind = WorkSourceKind.Anime, SourceId = starfall.Id });
        var manual = new Collection { ProfileId = Profile, Kind = CollectionKind.Manual, Name = "Weekend Picks", SortOrder = 1 };
        var smart = new Collection { ProfileId = Profile, Kind = CollectionKind.Smart, Name = "Airing now", SortOrder = 2 };
        var others = new Collection { ProfileId = "someone-else", Kind = CollectionKind.Manual, Name = "Not mine", SortOrder = 1 };
        host.Db.AddRange(manual, smart, others);
        host.Db.Add(new CollectionItem { CollectionId = manual.Id, WorkId = work.Id, Source = CollectionItemSource.Manual });
        await host.Db.SaveChangesAsync();

        var html = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library?section=collections", asOwner: false));

        StringAssert.Matches(
            Between(html, "<header class=\"lib-head\"", "</header>"),
            new Regex(@"lib-switch-item is-active""\s+href=""/Library\?section=collections"""));
        Assert.IsFalse(html.Contains("library-type-tabs", StringComparison.Ordinal), "Collections are not a media type.");
        Assert.IsFalse(html.Contains("lib-filter", StringComparison.Ordinal), "Filters belong to the Library view.");
        StringAssert.Contains(html, "2 collections");
        StringAssert.Contains(html, "Manage collections");
        Assert.IsFalse(html.Contains("Not mine", StringComparison.Ordinal), "Another profile's collections stay private.");

        var tiles = html.Split("<a class=\"lib-tile\"");
        Assert.AreEqual(3, tiles.Length, "Two tiles: the profile's own collections.");
        var first = tiles[1][..tiles[1].IndexOf("</a>", StringComparison.Ordinal)];
        StringAssert.Contains(first, "Weekend Picks");
        StringAssert.Contains(first, "1 item");
        StringAssert.Contains(first, "Manual");
        StringAssert.Contains(first, Cover);
        StringAssert.Contains(first, $"/Collections/{manual.Id}");

        var second = tiles[2][..tiles[2].IndexOf("</a>", StringComparison.Ordinal)];
        StringAssert.Contains(second, "Airing now");
        StringAssert.Contains(second, "0 items");
        StringAssert.Contains(second, "Smart");
        StringAssert.Contains(second, "lib-card-initial");
    }

    [TestMethod]
    public async Task EmptyCollectionsViewLinksToWhereCollectionsAreCreated()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();

        var html = WebUtility.HtmlDecode(await host.GetHtmlAsync("/Library?section=collections", asOwner: false));

        StringAssert.Contains(html, "No collections yet.");
        StringAssert.Contains(html, "Create a collection");
        StringAssert.Contains(html, "href=\"/Collections\"");
    }

    [TestMethod]
    public async Task EntriesCarryPlayableAndMissingUnitsFormatAndWhenTheTitleWasLastWatched()
    {
        var (host, starfall, amber) = await SeedAsync();
        await using var _host = host;

        var read = await new LibraryMediaCardQuery(host.Db).GetAnimeEntriesAsync(Profile, CancellationToken.None);

        Assert.IsFalse(read.Degraded);
        var starfallEntry = read.Entries.Single(x => x.Card.Href.EndsWith(starfall.Id.ToString(), StringComparison.Ordinal));
        Assert.AreEqual(2, starfallEntry.PlayableUnits);
        Assert.AreEqual(1, starfallEntry.MissingUnits, "Episode 3 is known but has no file.");
        Assert.AreEqual("TV", starfallEntry.Format);
        Assert.AreEqual(Cover, starfallEntry.PosterUrl);
        Assert.IsNotNull(starfallEntry.LastWatchedAt);

        var amberEntry = read.Entries.Single(x => x.Card.Href.EndsWith(amber.Id.ToString(), StringComparison.Ordinal));
        Assert.AreEqual(0, amberEntry.PlayableUnits);
        Assert.AreEqual(1, amberEntry.MissingUnits);
        Assert.IsNull(amberEntry.LastWatchedAt);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(from >= 0, $"Markup '{start}' was not rendered.");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? text[from..] : text[from..(to + end.Length)];
    }
}
