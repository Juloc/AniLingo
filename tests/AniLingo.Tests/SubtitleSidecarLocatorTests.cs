using AniLingo.Web.Features.Library;

namespace AniLingo.Tests;

[TestClass]
public sealed class SubtitleSidecarLocatorTests
{
    private const string BaseName = "Frieren - S01E01";

    [TestMethod]
    public void ClassifiesLanguageForcedDefaultAndSdhTokens()
    {
        var forced = Classify("Frieren - S01E01.ja.forced.ass");
        var standard = Classify("Frieren - S01E01.jpn.default.srt");
        var bracketed = Classify("Frieren - S01E01 [ja].srt");
        var sdh = Classify("Frieren - S01E01.japanese.sdh.srt");
        var regional = Classify("Frieren - S01E01.ja-JP.vtt");
        var signs = Classify("Frieren - S01E01.jpn.signs.ssa");

        Assert.IsNotNull(forced);
        Assert.IsTrue(forced.IsJapaneseTagged);
        Assert.IsTrue(forced.IsForced);
        Assert.AreEqual("ass", forced.Format);

        Assert.IsNotNull(standard);
        Assert.IsTrue(standard.IsJapaneseTagged);
        Assert.IsTrue(standard.IsDefault);
        Assert.IsFalse(standard.IsForced);

        Assert.IsNotNull(bracketed);
        Assert.IsTrue(bracketed.IsJapaneseTagged);

        Assert.IsNotNull(sdh);
        Assert.IsTrue(sdh.IsJapaneseTagged);
        Assert.IsTrue(sdh.IsHearingImpaired);

        Assert.IsNotNull(regional);
        Assert.IsTrue(regional.IsJapaneseTagged);
        Assert.AreEqual("vtt", regional.Format);

        Assert.IsNotNull(signs);
        Assert.IsTrue(signs.IsForced);
        Assert.AreEqual("ssa", signs.Format);
    }

    [TestMethod]
    public void UntaggedAndUnknownTokenFilesAreUntaggedCandidates()
    {
        var untagged = Classify("Frieren - S01E01.srt");
        var releaseGroup = Classify("Frieren - S01E01.SubsPlease.ass");

        Assert.IsNotNull(untagged);
        Assert.IsFalse(untagged.IsJapaneseTagged);
        Assert.IsNotNull(releaseGroup);
        Assert.IsFalse(releaseGroup.IsJapaneseTagged);
    }

    [TestMethod]
    public void RejectsOtherLanguagesUnrelatedNamesAndUnsupportedFormats()
    {
        Assert.IsNull(Classify("Frieren - S01E01.en.srt"));
        Assert.IsNull(Classify("Frieren - S01E01.eng.forced.ass"));
        Assert.IsNull(Classify("Frieren - S01E01 [zh-Hans].srt"));
        Assert.IsNull(Classify("Frieren - S01E010.ja.srt"));
        Assert.IsNull(Classify("Frieren - S01E02.ja.srt"));
        Assert.IsNull(Classify("Frieren - S01E01.ja.sub"));
        Assert.IsNull(Classify("Frieren - S01E01.ja.txt"));
        Assert.IsNull(Classify("._Frieren - S01E01.ja.srt"));
    }

    [TestMethod]
    public void PerEpisodeSidecarFolderAcceptsTrackStyleNames()
    {
        var japanese = SubtitleSidecarLocator.Classify(
            Path.Combine("Subs", BaseName, "3_Japanese.srt"),
            BaseName,
            SubtitleSidecarLocation.EpisodeSidecarDirectory);
        var english = SubtitleSidecarLocator.Classify(
            Path.Combine("Subs", BaseName, "2_English.srt"),
            BaseName,
            SubtitleSidecarLocation.EpisodeSidecarDirectory);
        var unrelatedInSharedDirectory = SubtitleSidecarLocator.Classify(
            Path.Combine("Subs", "3_Japanese.srt"),
            BaseName,
            SubtitleSidecarLocation.SidecarDirectory);

        Assert.IsNotNull(japanese);
        Assert.IsTrue(japanese.IsJapaneseTagged);
        Assert.IsNull(english);
        Assert.IsNull(unrelatedInSharedDirectory);
    }

    [TestMethod]
    public void OrdersTaggedFullDefaultNearestFormatThenOrdinalPath()
    {
        var candidates = new[]
        {
            Candidate("f.srt", tagged: false),
            Candidate("e.srt", forced: true),
            Candidate("d.srt", hearingImpaired: true),
            Candidate("c.vtt"),
            Candidate("b.ass"),
            Candidate("a2.srt", location: SubtitleSidecarLocation.SidecarDirectory),
            Candidate("a1.srt"),
            Candidate("a0.srt"),
            Candidate("z.srt", isDefault: true)
        };

        var ordered = SubtitleSidecarLocator.Order(Enumerable.Reverse(candidates))
            .Select(x => Path.GetFileName(x.Path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "z.srt", "a0.srt", "a1.srt", "b.ass", "c.vtt", "a2.srt", "d.srt", "e.srt", "f.srt" },
            ordered);
    }

    [TestMethod]
    public void DiscoversBoundedSidecarDirectoriesOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anilingo-sidecars-{Guid.NewGuid():N}");
        var season = Path.Combine(root, "Frieren", "Season 01");
        var mediaPath = Path.Combine(season, $"{BaseName}.mkv");

        try
        {
            Directory.CreateDirectory(Path.Combine(season, "subs", BaseName));
            Directory.CreateDirectory(Path.Combine(season, "Subtitles", "Nested"));
            Directory.CreateDirectory(Path.Combine(season, "Extras"));
            File.WriteAllBytes(mediaPath, [0]);
            File.WriteAllBytes(Path.Combine(season, "Frieren - S01E01.5.mkv"), [0]);

            Touch(season, "Frieren - S01E01.ja.forced.ass");
            Touch(season, "Frieren - S01E01.5.ja.srt");
            Touch(season, "Frieren - S01E01.en.srt");
            Touch(Path.Combine(season, "subs"), "Frieren - S01E01.jpn.srt");
            Touch(Path.Combine(season, "subs", BaseName), "3_Japanese.vtt");
            Touch(Path.Combine(season, "subs", BaseName), "2_English.srt");
            Touch(Path.Combine(season, "Subtitles"), "Frieren - S01E01.srt");
            Touch(Path.Combine(season, "Subtitles", "Nested"), "Frieren - S01E01.ja.srt");
            Touch(Path.Combine(season, "Extras"), "Frieren - S01E01.ja.srt");

            var found = SubtitleSidecarLocator.FindJapaneseCandidates([mediaPath])
                .Select(x => Path.GetRelativePath(season, x.Path))
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    Path.Combine("subs", "Frieren - S01E01.jpn.srt"),
                    Path.Combine("subs", BaseName, "3_Japanese.vtt"),
                    "Frieren - S01E01.ja.forced.ass",
                    Path.Combine("Subtitles", "Frieren - S01E01.srt")
                },
                found);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SubtitleSidecarCandidate? Classify(string fileName) =>
        SubtitleSidecarLocator.Classify(
            Path.Combine(Path.GetTempPath(), fileName),
            BaseName,
            SubtitleSidecarLocation.EpisodeDirectory);

    private static SubtitleSidecarCandidate Candidate(
        string fileName,
        bool tagged = true,
        bool forced = false,
        bool hearingImpaired = false,
        bool isDefault = false,
        SubtitleSidecarLocation location = SubtitleSidecarLocation.EpisodeDirectory) =>
        new(
            Path.Combine(Path.GetTempPath(), fileName),
            Path.GetExtension(fileName).TrimStart('.'),
            tagged,
            forced,
            hearingImpaired,
            isDefault,
            location);

    private static void Touch(string directory, string fileName) =>
        File.WriteAllText(Path.Combine(directory, fileName), "");
}
