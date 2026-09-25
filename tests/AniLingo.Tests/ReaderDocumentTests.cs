using AniLingo.Web.Features.ReaderCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class ReaderDocumentTests
{
    [DataTestMethod]
    [DataRow("NOVEL", "narou", ReaderContentType.LightNovel)]
    [DataRow("LIGHT_NOVEL", "narou", ReaderContentType.LightNovel)]
    [DataRow(null, "narou", ReaderContentType.WebNovel)]
    public void NovelMetadataResolvesContentType(
        string? format,
        string? sourceProvider,
        ReaderContentType expected)
    {
        Assert.AreEqual(
            expected,
            ReaderContentTypes.FromNovelMetadata(format, sourceProvider));
    }

    [TestMethod]
    public void ReflowReadersExposeSharedCapabilities()
    {
        var book = ReaderDocumentDescriptor.Create(
            Guid.NewGuid(),
            ReaderContentType.Book,
            "Book");
        var lightNovel = ReaderDocumentDescriptor.Create(
            Guid.NewGuid(),
            ReaderContentType.LightNovel,
            "LN");

        Assert.AreEqual(ReaderLayoutKind.ReflowableText, book.LayoutKind);
        Assert.IsTrue(book.Capabilities.SupportsTypography);
        Assert.IsTrue(book.Capabilities.SupportsPaged);
        Assert.IsTrue(lightNovel.Capabilities.SupportsContinuous);
        Assert.IsTrue(lightNovel.Capabilities.SupportsEmbeddedImages);
    }

    [TestMethod]
    public void BuiltInPresetsDifferByTypeWithoutChangingTheEngine()
    {
        var book = ReaderPresetCatalog.For(ReaderContentType.Book);
        var lightNovel = ReaderPresetCatalog.For(ReaderContentType.LightNovel);
        var webNovel = ReaderPresetCatalog.For(ReaderContentType.WebNovel);

        Assert.AreEqual("paged", book.ReadingMode);
        Assert.AreEqual("paged", lightNovel.ReadingMode);
        Assert.AreEqual("light-novel", lightNovel.ChapterStyle);
        Assert.AreEqual("continuous", webNovel.ReadingMode);
        Assert.IsFalse(webNovel.TwoPageSpread);
    }
}
