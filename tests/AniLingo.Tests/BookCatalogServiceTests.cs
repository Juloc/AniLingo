using AniLingo.Web.Features.Books;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookCatalogServiceTests
{
    [TestMethod]
    public void ReadableSampleStripsGutenbergBoilerplateAndSkipsDenseContents()
    {
        var raw = """
            Project Gutenberg header
            *** START OF THE PROJECT GUTENBERG EBOOK TEST ***

            CONTENTS

            CHAPTER I. One
            CHAPTER II. Two
            CHAPTER III. Three

            CHAPTER I. One

            This is the real first chapter. It has enough prose to be readable.
            Another paragraph continues the story and establishes the actual content.
            """ + new string('x', 1200) + """

            CHAPTER II. Two

            Later chapter.
            *** END OF THE PROJECT GUTENBERG EBOOK TEST ***
            trailing license
            """;

        var sample = BookCatalogService.ExtractReadableSample(raw, 1000);

        Assert.IsTrue(sample.StartsWith("CHAPTER I. One", StringComparison.Ordinal));
        Assert.IsTrue(sample.Contains("real first chapter", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("Project Gutenberg header", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("trailing license", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReadableSampleUsesBoundedTextWhenThereAreNoChapterHeadings()
    {
        var raw = "*** START OF TEST ***\n" + new string('a', 1400) + ".\n*** END OF TEST ***";

        var sample = BookCatalogService.ExtractReadableSample(raw, 700);

        Assert.IsTrue(sample.Length <= 700);
        Assert.IsFalse(sample.Contains("*** START", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("*** END", StringComparison.Ordinal));
    }
}
