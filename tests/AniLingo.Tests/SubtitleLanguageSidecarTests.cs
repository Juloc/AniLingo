using AniLingo.Web.Features.Library;

namespace AniLingo.Tests;

// Sidecar selection for non-Japanese target languages: SubtitleSidecarLocatorTests.cs
// keeps the Japanese-target behaviour unchanged; these tests cover the same
// mechanics for de/id targets, including the untagged-file rule that differs
// from Japanese (no kana-equivalent content heuristic exists for them).
[TestClass]
public sealed class SubtitleLanguageSidecarTests
{
    private const string BaseName = "Alltag - S01E01";

    [TestMethod]
    public void ClassifiesGermanTaggedFilesForAGermanTarget()
    {
        var tagged = Classify("Alltag - S01E01.de.srt", "de");
        var alsoTagged = Classify("Alltag - S01E01.german.srt", "de");
        var untagged = Classify("Alltag - S01E01.srt", "de");
        var otherLanguage = Classify("Alltag - S01E01.en.srt", "de");

        Assert.IsNotNull(tagged);
        Assert.IsTrue(tagged.IsTargetLanguageTagged);
        Assert.IsNotNull(alsoTagged);
        Assert.IsTrue(alsoTagged.IsTargetLanguageTagged);
        Assert.IsNotNull(untagged);
        Assert.IsFalse(untagged.IsTargetLanguageTagged);
        Assert.IsNull(otherLanguage);
    }

    [TestMethod]
    public void ClassifiesIndonesianTaggedFilesForAnIndonesianTarget()
    {
        var tagged = Classify("Alltag - S01E01.id.srt", "id");
        var alsoTagged = Classify("Alltag - S01E01.indonesian.srt", "id");
        var otherLanguage = Classify("Alltag - S01E01.de.srt", "id");

        Assert.IsNotNull(tagged);
        Assert.IsTrue(tagged.IsTargetLanguageTagged);
        Assert.IsNotNull(alsoTagged);
        Assert.IsTrue(alsoTagged.IsTargetLanguageTagged);
        Assert.IsNull(otherLanguage);
    }

    [TestMethod]
    public void UntaggedFileIsAcceptedOnlyWhenNoTaggedFileExistsForNonJapaneseTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anilingo-lang-sidecar-{Guid.NewGuid():N}");
        var mediaPath = Path.Combine(root, $"{BaseName}.mkv");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(mediaPath, [0]);
            Touch(root, $"{BaseName}.srt");
            Touch(root, $"{BaseName}.de.srt");

            var listings = new SubtitleSidecarDirectoryCache();
            var found = SubtitleSidecarLocator.FindCandidates([mediaPath], listings, "de");

            Assert.AreEqual(1, found.Count);
            Assert.IsTrue(found[0].IsTargetLanguageTagged);
            StringAssert.EndsWith(found[0].Path, $"{BaseName}.de.srt");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void UntaggedFileIsOfferedForNonJapaneseTargetsWhenNothingIsTagged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anilingo-lang-sidecar-{Guid.NewGuid():N}");
        var mediaPath = Path.Combine(root, $"{BaseName}.mkv");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(mediaPath, [0]);
            Touch(root, $"{BaseName}.srt");

            var listings = new SubtitleSidecarDirectoryCache();
            var found = SubtitleSidecarLocator.FindCandidates([mediaPath], listings, "de");

            Assert.AreEqual(1, found.Count);
            Assert.IsFalse(found[0].IsTargetLanguageTagged);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void UntaggedFileTaggedAsAnotherKnownLanguageIsNeverACandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anilingo-lang-sidecar-{Guid.NewGuid():N}");
        var mediaPath = Path.Combine(root, $"{BaseName}.mkv");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(mediaPath, [0]);
            Touch(root, $"{BaseName}.en.srt");

            var listings = new SubtitleSidecarDirectoryCache();
            var found = SubtitleSidecarLocator.FindCandidates([mediaPath], listings, "de");

            Assert.AreEqual(0, found.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SubtitleSidecarCandidate? Classify(string fileName, string targetLanguageTag) =>
        SubtitleSidecarLocator.Classify(
            Path.Combine(Path.GetTempPath(), fileName),
            BaseName,
            SubtitleSidecarLocation.EpisodeDirectory,
            targetLanguageTag);

    private static void Touch(string directory, string fileName) =>
        File.WriteAllText(Path.Combine(directory, fileName), "");
}
