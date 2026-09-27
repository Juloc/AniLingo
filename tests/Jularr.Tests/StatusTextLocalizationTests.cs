using System.Text.RegularExpressions;
using Jularr.Web.Features.Localization;

namespace Jularr.Tests;

/// <summary>Status messages set by page handlers come from UiTranslationResources.</summary>
[TestClass]
public sealed class StatusTextLocalizationTests
{
    [TestMethod]
    [DataRow("Learn", "Review.cshtml.cs")]
    [DataRow("Settings", "ChapterArtwork.cshtml.cs")]
    public void StatusTextIsNotHardCoded(string folder, string file)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Jularr.Web", "Pages", folder, file));

        Assert.IsFalse(
            Regex.IsMatch(source, @"TempData\[""Status""\]\s*=\s*\$?"""),
            $"{file} sets a literal status text.");
        Assert.IsFalse(source.Contains("Kapitel", StringComparison.Ordinal), $"{file} contains German UI text.");
    }

    [TestMethod]
    public void StatusKeysExistInTheCatalog()
    {
        var english = UiTextBundle.English;
        foreach (var key in new[]
                 {
                     "learn.review.sourceChapter",
                     "learn.review.status.noSentence",
                     "learn.review.status.explanationReady",
                     "learn.review.status.explanationFailed",
                     "learn.review.status.cardNotDue",
                     "settings.chapterArtwork.saved",
                     "settings.chapterArtwork.storageRootNotAbsolute"
                 })
        {
            Assert.AreNotEqual(key, english[key], $"{key} is missing from UiTranslationResources.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
