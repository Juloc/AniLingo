using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Naming;
using Jularr.Web.Features.ReadingAcquisition;

namespace Jularr.Tests;

[TestClass]
public sealed class ReadingNamingFormatterTests
{
    [TestMethod]
    public void DefaultProfileMatchesLegacySafeNamePlacement()
    {
        foreach (var kind in ReadingNamingPresets.ReadingKinds)
        {
            var profile = ReadingNamingPresets.Default(kind);
            var title = "Re:Zero − Starting Life/Volume";

            var folder = ReadingNamingFormatter.BuildSeriesFolderName(profile, new ReadingNamingRequest(kind, Series: title));
            Assert.AreEqual(MangaLibraryPlacement.SafeName(title), folder, $"{kind} series folder should match legacy SafeName placement.");

            var file = ReadingNamingFormatter.BuildFileName(profile, new ReadingNamingRequest(kind, OriginalFileName: "My Chapter 01"));
            Assert.AreEqual("My Chapter 01", file, $"{kind} default file format should preserve the original name.");
        }
    }

    [TestMethod]
    public void SeriesAuthorAndLanguageTokensRenderInAnyScope()
    {
        var profile = new ReadingNamingProfile(MediaAcquisitionKind.Book, "Custom", "{Author}", "{Series} by {Author} ({Language})");
        var request = new ReadingNamingRequest(MediaAcquisitionKind.Book, Series: "The Sample", Author: "Jane Doe", Language: "en");

        Assert.AreEqual("Jane Doe", ReadingNamingFormatter.BuildSeriesFolderName(profile, request));
        Assert.AreEqual("The Sample by Jane Doe (en)", ReadingNamingFormatter.BuildFileName(profile, request));
    }

    [TestMethod]
    public void FileOnlyTokensAreRejectedInSeriesFolderScope()
    {
        var profile = new ReadingNamingProfile(MediaAcquisitionKind.Manga, "Custom", "{Series} {Chapter Number:000}", "{Series}");

        var errors = ReadingNamingFormatter.Validate(profile, ReadingNamingSamples.Request(MediaAcquisitionKind.Manga));

        Assert.IsTrue(errors.Any(error => error.Contains("chapter number", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void VolumeAndChapterNumbersZeroPad()
    {
        var profile = new ReadingNamingProfile(
            MediaAcquisitionKind.LightNovel,
            "Custom",
            "{Series}",
            "{Series} - Volume {Volume Number:00}");
        var request = new ReadingNamingRequest(MediaAcquisitionKind.LightNovel, Series: "Sample", VolumeNumber: 3);

        Assert.AreEqual("Sample - Volume 03", ReadingNamingFormatter.BuildFileName(profile, request));

        var mangaProfile = new ReadingNamingProfile(
            MediaAcquisitionKind.Manga,
            "Custom",
            "{Series}",
            "{Series} - Chapter {Chapter Number:000}");
        var chapterRequest = new ReadingNamingRequest(MediaAcquisitionKind.Manga, Series: "Sample", ChapterNumber: 12.5);

        Assert.AreEqual("Sample - Chapter 012.5", ReadingNamingFormatter.BuildFileName(mangaProfile, chapterRequest));
    }

    [TestMethod]
    public void UnresolvedTokensRenderEmptyAndTrailingSeparatorsAreTrimmed()
    {
        // No optional-bracket syntax is supported (unlike anime naming): an unresolved token
        // simply renders as nothing, and the trailing separator left behind is trimmed away.
        var profile = new ReadingNamingProfile(MediaAcquisitionKind.Book, "Custom", "{Series}", "{Series}-{Volume Number:00}");
        var request = new ReadingNamingRequest(MediaAcquisitionKind.Book, Series: "Sample");

        Assert.AreEqual("Sample", ReadingNamingFormatter.BuildFileName(profile, request));
    }

    [TestMethod]
    public void IllegalCharactersAreSanitizedToUnderscoreLikeLegacyPlacement()
    {
        var profile = ReadingNamingPresets.Structured(MediaAcquisitionKind.Book);
        var request = new ReadingNamingRequest(MediaAcquisitionKind.Book, Author: "A/B: C");

        var folder = ReadingNamingFormatter.BuildSeriesFolderName(profile, request);

        Assert.AreEqual(MangaLibraryPlacement.SafeName("A/B: C"), folder);
        Assert.IsFalse(folder.Contains('/'));
        Assert.IsFalse(folder.Contains(':'));
    }

    [TestMethod]
    public void EmptyTemplateFailsValidation()
    {
        var profile = ReadingNamingPresets.Default(MediaAcquisitionKind.Manga) with { FileFormat = "" };

        var errors = ReadingNamingFormatter.Validate(profile, ReadingNamingSamples.Request(MediaAcquisitionKind.Manga));

        Assert.IsTrue(errors.Count > 0);
    }
}
