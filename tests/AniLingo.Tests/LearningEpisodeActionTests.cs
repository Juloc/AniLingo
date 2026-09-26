using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Pages.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// #339: on the Episode page, Save and Known need Vocabulary; Learn queues
/// spaced repetition and additionally needs Reviews for the episode scope.
/// </summary>
[TestClass]
public sealed class LearningEpisodeActionTests
{
    [TestMethod]
    public async Task VocabularyWithoutReviewsCannotMoveAnEpisodeTermToLearning()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Custom);
        await fixture.SetProfileCapabilityAsync(LearningCapability.Vocabulary, true);
        var term = await AddTermAsync(fixture, "猫");

        Assert.IsInstanceOfType<ForbidResult>(
            await Page(fixture).OnPostLearningAsync(fixture.EpisodeId, term, CancellationToken.None));
        Assert.AreEqual(0, await fixture.Db.LearningCards.CountAsync());

        Assert.IsInstanceOfType<RedirectToPageResult>(
            await Page(fixture).OnPostSavedAsync(fixture.EpisodeId, term, CancellationToken.None));
        var card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Saved, card.State);
        Assert.IsNull(card.NextReviewAt);
        Assert.IsNull(card.QueuePosition);

        Assert.IsInstanceOfType<RedirectToPageResult>(
            await Page(fixture).OnPostKnownAsync(fixture.EpisodeId, term, CancellationToken.None));
        Assert.AreEqual(UserTermState.Known, (await fixture.WordCardAsync("猫")).State);
    }

    [TestMethod]
    public async Task ReviewsForTheEpisodeScopeAllowLearning()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Custom);
        await fixture.SetProfileCapabilityAsync(LearningCapability.Vocabulary, true);
        await new LearningConfigurationStore(fixture.Db).SetCapabilityOverrideAsync(
            LanguageInspectorFixture.Profile,
            LearningScopeRef.ForContent(LearningMediaType.Anime, fixture.EpisodeId.ToString()),
            LearningCapability.Reviews,
            true,
            CancellationToken.None);
        var term = await AddTermAsync(fixture, "猫");

        Assert.IsInstanceOfType<RedirectToPageResult>(
            await Page(fixture).OnPostLearningAsync(fixture.EpisodeId, term, CancellationToken.None));
        var card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Learning, card.State);
        Assert.IsNotNull(card.QueuePosition);
    }

    [TestMethod]
    public async Task OffRefusesEveryEpisodeWordAction()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        var term = await AddTermAsync(fixture, "猫");

        Assert.IsInstanceOfType<ForbidResult>(
            await Page(fixture).OnPostSavedAsync(fixture.EpisodeId, term, CancellationToken.None));
        Assert.IsInstanceOfType<ForbidResult>(
            await Page(fixture).OnPostKnownAsync(fixture.EpisodeId, term, CancellationToken.None));
        Assert.AreEqual(0, await fixture.Db.LearningCards.CountAsync());
    }

    [TestMethod]
    public void TheLearnButtonIsHiddenWithoutReviews()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AniLingo.sln")))
        {
            root = root.Parent;
        }

        var view = File.ReadAllText(Path.Combine(
            root!.FullName,
            "src",
            "AniLingo.Web",
            "Pages",
            "Library",
            "Episode.cshtml"));
        StringAssert.Contains(view, "hidden=\"@(!Model.ShowLearnAction)\"");
    }

    private static EpisodeModel Page(LanguageInspectorFixture fixture) =>
        new(
            fixture.Db,
            LearningTestData.Service(fixture.Db, LanguageInspectorFixture.Profile),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            TestAccounts.Context(LanguageInspectorFixture.Profile),
            null!);

    private static async Task<Guid> AddTermAsync(LanguageInspectorFixture fixture, string canonical)
    {
        var term = new AniLingo.Web.Features.Vocabulary.Term { Language = "ja", Canonical = canonical };
        fixture.Db.Terms.Add(term);
        await fixture.Db.SaveChangesAsync();
        return term.Id;
    }
}
