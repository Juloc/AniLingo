using AniLingo.Web.Features.ReaderCore;
using Xunit;

namespace AniLingo.Tests;

public sealed class ReaderDocumentTests
{
    [Theory]
    [InlineData("NOVEL", "narou", ReaderContentType.LightNovel)]
    [InlineData("LIGHT_NOVEL", "narou", ReaderContentType.LightNovel)]
    [InlineData(null, "narou", ReaderContentType.WebNovel)]
    public void NovelMetadataResolvesContentType(
        string? format,
        string? sourceProvider,
        ReaderContentType expected)
    {
        Assert.Equal(
            expected,
            ReaderContentTypes.FromNovelMetadata(format, sourceProvider));
    }

    [Fact]
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

        Assert.Equal(ReaderLayoutKind.ReflowableText, book.LayoutKind);
        Assert.True(book.Capabilities.SupportsTypography);
        Assert.True(book.Capabilities.SupportsPaged);
        Assert.True(lightNovel.Capabilities.SupportsContinuous);
        Assert.True(lightNovel.Capabilities.SupportsEmbeddedImages);
    }

    [Fact]
    public void BuiltInPresetsDifferByTypeWithoutChangingTheEngine()
    {
        var book = ReaderPresetCatalog.For(ReaderContentType.Book);
        var lightNovel = ReaderPresetCatalog.For(ReaderContentType.LightNovel);
        var webNovel = ReaderPresetCatalog.For(ReaderContentType.WebNovel);

        Assert.Equal("paged", book.ReadingMode);
        Assert.Equal("paged", lightNovel.ReadingMode);
        Assert.Equal("light-novel", lightNovel.ChapterStyle);
        Assert.Equal("continuous", webNovel.ReadingMode);
        Assert.False(webNovel.TwoPageSpread);
    }
}
