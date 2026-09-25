using AniLingo.Web.Features.Learning;
using AniLingo.Web.Pages.Settings;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningScopePageTests
{
    [TestMethod]
    public void WorkScopeUsesMediaTypeAndWorkKey()
    {
        var valid = LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Anime,
            "work",
            "anime-42",
            null,
            out var scope);

        Assert.IsTrue(valid);
        Assert.AreEqual(LearningScopeKind.Work, scope.Kind);
        Assert.AreEqual("Anime:anime-42", scope.Key);
    }

    [TestMethod]
    public void ContentScopeUsesContentKey()
    {
        var valid = LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Anime,
            "content",
            "anime-42",
            "episode-7",
            out var scope);

        Assert.IsTrue(valid);
        Assert.AreEqual(LearningScopeKind.Content, scope.Kind);
        Assert.AreEqual("Anime:episode-7", scope.Key);
    }

    [TestMethod]
    public void ContentScopeRequiresContentKey()
    {
        var valid = LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Book,
            "content",
            "book-42",
            null,
            out _);

        Assert.IsFalse(valid);
    }

    [TestMethod]
    public void WorkScopeRequiresWorkKey()
    {
        var valid = LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Manga,
            "work",
            null,
            null,
            out _);

        Assert.IsFalse(valid);
    }

    [TestMethod]
    public void ScopeKeysRemainDistinctAcrossMediaTypes()
    {
        Assert.IsTrue(LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Novel,
            "work",
            "same-id",
            null,
            out var novel));

        Assert.IsTrue(LearningScopeModel.TryBuildScopeReference(
            LearningMediaType.Book,
            "work",
            "same-id",
            null,
            out var book));

        Assert.AreNotEqual(novel.Key, book.Key);
        Assert.AreEqual("Novel:same-id", novel.Key);
        Assert.AreEqual("Book:same-id", book.Key);
    }
}
