using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Tests;

[TestClass]
public sealed class CompletedDownloadImportPlannerTests
{
    private static readonly AnimeQualityProfile Profile = AnimeQualityProfiles.CreateDefaultAnime1080p();

    [TestMethod]
    public void AutoImportsExactSeasonEpisodeAndMatchingSidecar()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);
        var files = new[]
        {
            new CompletedDownloadFile("/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv", 900_000_000),
            new CompletedDownloadFile("/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].de.srt", 100_000)
        };

        var plan = CompletedDownloadImportPlanner.Plan(context, files);

        var item = plan.Files.Single(file => file.Source.Path.EndsWith(".mkv"));
        Assert.AreEqual(AnimeImportDisposition.AutoImport, item.Disposition);
        Assert.AreEqual(1, item.Targets.Count);
        Assert.AreEqual(1, item.Targets[0].EpisodeNumber);
        Assert.AreEqual(1, item.SidecarPaths.Count);
        Assert.IsFalse(plan.RequiresManualIntervention);
    }

    [TestMethod]
    public void MapsAbsoluteAnimeEpisodeToLocalSeasonEpisode()
    {
        var context = Context(
            [new RequestedAnimeEpisode(2, 1, 13)],
            aliases: ["Anime Name"]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [new CompletedDownloadFile("/downloads/[Group] Anime Name - 13 WEB-DL 1080p HEVC AAC[JA].mkv", 800_000_000)]);

        var item = plan.Files.Single();
        Assert.AreEqual(AnimeImportDisposition.AutoImport, item.Disposition);
        Assert.AreEqual(2, item.Targets[0].SeasonNumber);
        Assert.AreEqual(1, item.Targets[0].EpisodeNumber);
        Assert.AreEqual(13, item.Targets[0].AbsoluteEpisodeNumber);
    }

    [TestMethod]
    public void MapsMultiEpisodeFileOnlyWhenWholeRequestedRangeMatches()
    {
        var context = Context(
            [
                new RequestedAnimeEpisode(1, 1),
                new RequestedAnimeEpisode(1, 2),
                new RequestedAnimeEpisode(1, 3)
            ]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [new CompletedDownloadFile("/downloads/Anime Name - S01E01-E03 WEB-DL 1080p AVC AAC[JA].mkv", 2_400_000_000)]);

        var item = plan.Files.Single();
        Assert.AreEqual(AnimeImportDisposition.AutoImport, item.Disposition);
        Assert.AreEqual(3, item.Targets.Count);
    }

    [TestMethod]
    public void RequiresManualReviewWhenParsedEpisodeWasNotRequested()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [new CompletedDownloadFile("/downloads/Anime Name - S01E02 WEB-DL 1080p AVC AAC[JA].mkv", 800_000_000)]);

        var item = plan.Files.Single();
        Assert.AreEqual(AnimeImportDisposition.ManualReview, item.Disposition);
        Assert.IsTrue(plan.RequiresManualIntervention);
    }

    [TestMethod]
    public void RequiresManualReviewForSingleFileWithoutEpisodeEvidence()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [new CompletedDownloadFile("/downloads/Anime Name WEB-DL 1080p AVC AAC[JA].mkv", 800_000_000)]);

        var item = plan.Files.Single();
        Assert.AreEqual(AnimeImportDisposition.ManualReview, item.Disposition);
        Assert.AreEqual(0.70, item.Confidence, 0.001);
    }

    [TestMethod]
    public void RequiresManualReviewWhenSeriesAliasDoesNotMatch()
    {
        var context = Context(
            [new RequestedAnimeEpisode(1, 1)],
            aliases: ["Expected Anime"]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [new CompletedDownloadFile("/downloads/Wrong Anime - S01E01 WEB-DL 1080p AVC AAC[JA].mkv", 800_000_000)]);

        Assert.AreEqual(AnimeImportDisposition.ManualReview, plan.Files.Single().Disposition);
    }

    [TestMethod]
    public void IgnoresCandidateWhenExistingFileIsEqualOrPreferred()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);
        var candidate = new CompletedDownloadFile(
            "/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv",
            800_000_000);
        var existing = new ExistingAnimeFile(
            "/library/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv",
            1,
            1,
            800_000_000);

        var plan = CompletedDownloadImportPlanner.Plan(context, [candidate], [existing]);

        Assert.AreEqual(AnimeImportDisposition.Ignore, plan.Files.Single().Disposition);
    }

    [TestMethod]
    public void AutoImportsUpgradeButPreservesOldPathUntilCommit()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);
        var candidate = new CompletedDownloadFile(
            "/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv",
            900_000_000);
        var existing = new ExistingAnimeFile(
            "/library/Anime Name - S01E01 WEB-DL 720p AVC AAC[JA].mkv",
            1,
            1,
            500_000_000);

        var plan = CompletedDownloadImportPlanner.Plan(context, [candidate], [existing]);

        var item = plan.Files.Single();
        Assert.AreEqual(AnimeImportDisposition.AutoImport, item.Disposition);
        CollectionAssert.AreEqual(new[] { existing.Path }, item.ExistingPathsToReplaceAfterCommit.ToArray());
        Assert.IsTrue(item.Reasons.Any(reason => reason.Contains("preserved", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DoesNotAttachUnrelatedSidecars()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);
        var files = new[]
        {
            new CompletedDownloadFile("/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv", 900_000_000),
            new CompletedDownloadFile("/downloads/Other Anime - S01E01.de.srt", 100_000)
        };

        var item = CompletedDownloadImportPlanner.Plan(context, files).Files.Single(file => file.Source.Path.EndsWith(".mkv"));

        Assert.AreEqual(0, item.SidecarPaths.Count);
    }

    [TestMethod]
    public void IgnoresUnsupportedFiles()
    {
        var context = Context([new RequestedAnimeEpisode(1, 1)]);

        var plan = CompletedDownloadImportPlanner.Plan(
            context,
            [
                new CompletedDownloadFile("/downloads/Anime Name - S01E01 WEB-DL 1080p AVC AAC[JA].mkv", 900_000_000),
                new CompletedDownloadFile("/downloads/readme.txt", 500)
            ]);

        Assert.IsTrue(plan.Files.Any(file =>
            file.Source.Path.EndsWith(".txt") &&
            file.Disposition == AnimeImportDisposition.Ignore));
    }

    private static CompletedDownloadImportContext Context(
        IReadOnlyList<RequestedAnimeEpisode> episodes,
        IReadOnlyList<string>? aliases = null) =>
        new(
            "job-1",
            "anime-name",
            aliases ?? ["Anime Name"],
            episodes,
            Profile);
}
