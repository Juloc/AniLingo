using System.Text.Json.Nodes;
using AniLingo.Web.Features.Discovery;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Tracking;

namespace AniLingo.Tests;

[TestClass]
public sealed class DiscoveryTests
{
    [TestMethod]
    public void SearchInputIsNormalizedAndForcesSearchMode()
    {
        var request = DiscoveryRequest.Parse(
            "  Mushoku   Tensei  ",
            "light-novel",
            "top");

        Assert.AreEqual("Mushoku Tensei", request.Query);
        Assert.AreEqual(DiscoveryCategory.LightNovel, request.Category);
        Assert.AreEqual(DiscoveryMode.Search, request.Mode);
    }

    [TestMethod]
    public void PersonalDiscoveryCacheKeyIsProfileScoped()
    {
        var request = DiscoveryRequest.Parse(
            null,
            "all",
            "my-list");

        Assert.AreNotEqual(
            request.CacheKey("owner"),
            request.CacheKey("learner-1"));
    }

    [TestMethod]
    public void AniListReadingDiscoverySeparatesNovelsAndManga()
    {
        const string json = """
        {
          "data": {
            "Page": {
              "media": [
                {
                  "id": 1,
                  "title": { "english": "Novel A", "romaji": "Novel A", "native": "小説A" },
                  "description": "Novel",
                  "coverImage": { "large": "https://example.invalid/novel.jpg" },
                  "bannerImage": null,
                  "format": "NOVEL",
                  "status": "FINISHED",
                  "chapters": 12,
                  "volumes": 2,
                  "startDate": { "year": 2020 },
                  "genres": ["Fantasy"],
                  "isAdult": false
                },
                {
                  "id": 2,
                  "title": { "english": "Manga B", "romaji": "Manga B", "native": "漫画B" },
                  "description": "Manga",
                  "coverImage": { "large": "https://example.invalid/manga.jpg" },
                  "bannerImage": null,
                  "format": "MANGA",
                  "status": "RELEASING",
                  "chapters": 50,
                  "volumes": 6,
                  "startDate": { "year": 2021 },
                  "genres": ["Action"],
                  "isAdult": false
                }
              ]
            }
          }
        }
        """;

        var novels = NovelAniListProvider.ParseReadingMediaResponse(
            json,
            includeNovels: true,
            includeManga: false);
        var manga = NovelAniListProvider.ParseReadingMediaResponse(
            json,
            includeNovels: false,
            includeManga: true);

        Assert.HasCount(1, novels);
        Assert.IsTrue(novels[0].IsNovel);
        Assert.AreEqual("Novel A", novels[0].PreferredTitle);

        Assert.HasCount(1, manga);
        Assert.IsFalse(manga[0].IsNovel);
        Assert.AreEqual("Manga B", manga[0].PreferredTitle);
    }

    [TestMethod]
    public void ParsesPersonalAniListLibraryAcrossAnimeAndNovels()
    {
        const string json = """
        {
          "data": {
            "MediaListCollection": {
              "lists": [
                {
                  "status": "CURRENT",
                  "entries": [
                    {
                      "id": 10,
                      "status": "CURRENT",
                      "progress": 5,
                      "repeat": 0,
                      "updatedAt": 200,
                      "media": {
                        "id": 100,
                        "type": "MANGA",
                        "format": "NOVEL",
                        "title": {
                          "english": "Light Novel",
                          "romaji": "Light Novel",
                          "native": "ライトノベル"
                        },
                        "coverImage": { "large": "https://example.invalid/ln.jpg" },
                        "status": "RELEASING",
                        "episodes": null,
                        "chapters": 20,
                        "volumes": 4,
                        "seasonYear": null,
                        "startDate": { "year": 2024 },
                        "genres": ["Fantasy"],
                        "isAdult": false
                      }
                    }
                  ]
                }
              ]
            }
          }
        }
        """;

        var items = AniListAccountService.ParseLibraryResponse(json);

        Assert.HasCount(1, items);
        Assert.IsTrue(items[0].IsNovel);
        Assert.AreEqual(5, items[0].Progress);
        Assert.AreEqual(20, items[0].TotalProgress);
        Assert.AreEqual("CURRENT", items[0].ListStatus);
        Assert.AreEqual(2024, items[0].Year);
    }

    [TestMethod]
    public void NovelProgressSyncNeverMovesBackward()
    {
        var preview = AniListAccountService.EvaluateRemoteChapterProgressSafety(
            Remote(progress: 8, status: "CURRENT"),
            requestedProgress: 7,
            aniListChapterCount: 20,
            mediaTitle: "Novel");

        Assert.IsFalse(preview.CanSync);
        Assert.IsTrue(preview.IsNoOp);
        Assert.AreEqual(8, preview.RemoteProgress);
    }

    [TestMethod]
    public void NovelProgressSyncRequiresCurrentAndAvoidsFinalChapter()
    {
        var paused = AniListAccountService.EvaluateRemoteChapterProgressSafety(
            Remote(progress: 3, status: "PAUSED"),
            requestedProgress: 4,
            aniListChapterCount: 20,
            mediaTitle: "Novel");

        Assert.IsFalse(paused.CanSync);
        Assert.IsFalse(paused.IsNoOp);

        var final = AniListAccountService.EvaluateRemoteChapterProgressSafety(
            Remote(progress: 19, status: "CURRENT"),
            requestedProgress: 20,
            aniListChapterCount: 20,
            mediaTitle: "Novel");

        Assert.IsFalse(final.CanSync);
        Assert.IsFalse(final.IsNoOp);
        StringAssert.Contains(final.Message, "final");
    }

    [TestMethod]
    public void DiscoveryClientUsesDebounceAndCancelsStaleRequests()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "js",
            "discover.js"));

        StringAssert.Contains(script, "setTimeout(() => load(false), 250)");
        StringAssert.Contains(script, "AbortController");
        StringAssert.Contains(script, "requestVersion");
        StringAssert.Contains(script, "popstate");
    }

    private static AniListRemoteListEntry Remote(
        int progress,
        string status) =>
        new(
            Id: 123,
            UserId: 42,
            MediaId: 999,
            Status: status,
            Progress: progress,
            Score: 7,
            Repeat: 0,
            Priority: 0,
            Private: false,
            Notes: null,
            HiddenFromStatusLists: false,
            CustomLists: JsonNode.Parse("""{"Favorites":true}"""),
            AdvancedScores: null,
            StartedAt: null,
            CompletedAt: null,
            UpdatedAt: 123456789);

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
