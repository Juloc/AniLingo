using AniLingo.Web.Features.Acquisition;
using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Tests;

[TestClass]
public sealed class AnimeReleaseScoringTests
{
    [TestMethod]
    public void DefaultProfileAcceptsTypical1080pWebRelease()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p();
        var release = AnimeReleaseParser.Parse(
            "[Group] Anime - 01 WEB-DL 1080p HEVC 10bit AAC[JA] [EN+DE]");

        var result = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(release, SizeBytes: 1_500_000_000));

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual("WEB-1080p", result.QualityKey);
        Assert.AreEqual(0, result.Score);
    }

    [TestMethod]
    public void RejectsDisallowedQualityAndSize()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            MinimumSizeBytes = 500,
            MaximumSizeBytes = 1_000
        };
        var release = AnimeReleaseParser.Parse(
            "[Group] Anime - 01 WEB-DL 2160p HEVC AAC");

        var result = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(release, SizeBytes: 1_500));

        Assert.IsFalse(result.Accepted);
        Assert.IsTrue(result.RejectionReasons.Any(reason => reason.Contains("not allowed", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.RejectionReasons.Any(reason => reason.Contains("above maximum", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AppliesTermsRegexAndWeightedRulesWithReasons()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            MinimumScore = 40,
            MustContain = ["WEB-DL"],
            MustNotContain = ["CAM"],
            RequiredRegex = [@"\b1080p\b"],
            RejectedRegex = [@"\bDUBBED-ONLY\b"],
            ScoreRules =
            [
                new(
                    "Preferred group",
                    AnimeReleaseRuleField.ReleaseGroup,
                    AnimeReleaseRuleMatch.Equals,
                    "GoodGroup",
                    20),
                new(
                    "German audio",
                    AnimeReleaseRuleField.AudioLanguage,
                    AnimeReleaseRuleMatch.Equals,
                    "DE",
                    25),
                new(
                    "HEVC",
                    AnimeReleaseRuleField.VideoCodec,
                    AnimeReleaseRuleMatch.Equals,
                    "Hevc",
                    5)
            ]
        };
        var release = AnimeReleaseParser.Parse(
            "[GoodGroup] Anime - 01 WEB-DL 1080p HEVC AAC[DE+JA] [EN+DE] Dual Audio");

        var result = AnimeReleaseScorer.Score(profile, new AnimeReleaseCandidate(release));

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(50, result.Score);
        CollectionAssert.AreEqual(
            new[] { "Preferred group: +20", "German audio: +25", "HEVC: +5" },
            result.ScoreReasons.ToArray());
    }

    [TestMethod]
    public void MinimumScoreRejectsOtherwiseValidRelease()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            MinimumScore = 10,
            ScoreRules = []
        };
        var release = AnimeReleaseParser.Parse(
            "[Group] Anime - 01 WEB-DL 1080p AVC AAC");

        var result = AnimeReleaseScorer.Score(profile, new AnimeReleaseCandidate(release));

        Assert.IsFalse(result.Accepted);
        Assert.IsTrue(result.RejectionReasons.Any(reason => reason.Contains("below minimum", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void RankPrefersQualityThenCustomScore()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            ScoreRules =
            [
                new(
                    "Preferred group",
                    AnimeReleaseRuleField.ReleaseGroup,
                    AnimeReleaseRuleMatch.Equals,
                    "Preferred",
                    100)
            ]
        };

        var webPreferred = new AnimeReleaseCandidate(
            AnimeReleaseParser.Parse("[Preferred] Anime - 01 WEB-DL 1080p AVC AAC"));
        var blurayOther = new AnimeReleaseCandidate(
            AnimeReleaseParser.Parse("[Other] Anime - 01 BluRay 1080p AVC AAC"));

        var ranked = AnimeReleaseScorer.Rank(profile, [webPreferred, blurayOther]);

        Assert.AreSame(blurayOther, ranked[0].Candidate);
        Assert.AreEqual("BLURAY-1080p", ranked[0].QualityKey);
    }

    [TestMethod]
    public void UpgradeStopsAtConfiguredCutoff()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p();
        var current = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(
                AnimeReleaseParser.Parse("[A] Anime - 01 BluRay 1080p AVC AAC")));
        var candidate = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(
                AnimeReleaseParser.Parse("[B] Anime - 01 BluRay 1080p AVC AAC Proper")));

        Assert.IsFalse(AnimeReleaseScorer.IsUpgrade(profile, current, candidate));
    }

    [TestMethod]
    public void SameQualityCanUpgradeByScoreBeforeCutoff()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            UpgradeCutoffQuality = "WEB-1080p"
        };
        var current = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(
                AnimeReleaseParser.Parse("[A] Anime - 01 WEB-DL 720p AVC AAC")));
        var candidate = AnimeReleaseScorer.Score(
            profile,
            new AnimeReleaseCandidate(
                AnimeReleaseParser.Parse("[B] Anime - 01 WEB-DL 720p AVC AAC Proper")));

        Assert.IsTrue(AnimeReleaseScorer.IsUpgrade(profile, current, candidate));
    }

    [TestMethod]
    public async Task StorePersistsNamedProfileAndPerAnimeAssignment()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new AnimeQualityProfileStore(directory);
            var custom = AnimeQualityProfiles.CreateDefaultAnime1080p() with
            {
                Id = "german-dual",
                Name = "German Dual Audio",
                MinimumScore = 25,
                ScoreRules =
                [
                    new(
                        "German audio",
                        AnimeReleaseRuleField.AudioLanguage,
                        AnimeReleaseRuleMatch.Equals,
                        "DE",
                        25)
                ]
            };
            var animeId = Guid.NewGuid();

            await store.UpsertAsync(custom);
            await store.AssignAnimeAsync(animeId, custom.Id);

            var reloaded = new AnimeQualityProfileStore(directory);
            var resolved = await reloaded.ResolveAsync(animeId);

            Assert.AreEqual("german-dual", resolved.Id);
            Assert.AreEqual(25, resolved.MinimumScore);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task StoreUsesDefaultProfileForUnassignedAnime()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new AnimeQualityProfileStore(directory);

            var resolved = await store.ResolveAsync(Guid.NewGuid());

            Assert.AreEqual(AnimeQualityProfiles.DefaultAnime1080pId, resolved.Id);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void InvalidRegexIsRejectedBeforeScoring()
    {
        var profile = AnimeQualityProfiles.CreateDefaultAnime1080p() with
        {
            RequiredRegex = ["["]
        };
        var release = AnimeReleaseParser.Parse("[Group] Anime - 01 WEB-DL 1080p AVC AAC");

        Assert.ThrowsExactly<ArgumentException>(() =>
            AnimeReleaseScorer.Score(profile, new AnimeReleaseCandidate(release)));
    }

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-quality-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }
}
