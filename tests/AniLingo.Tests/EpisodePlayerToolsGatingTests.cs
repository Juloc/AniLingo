using System.Reflection;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Pages.Library;

namespace AniLingo.Tests;

/// <summary>
/// #230: the Episode page's own player learning sheet (episode-player.js'
/// fallback for scopes where the shared language inspector did not render,
/// see <c>Model.ShowPlayerTools</c>) must need the PlayerTools capability
/// *and* at least one of Lookup/ReadingAids/AiExplanations, mirroring the
/// shared inspector's own "show at all" rule. Before this fix a Custom scope
/// that enabled only PlayerTools (leaving every language-tools capability
/// off) still rendered the fallback sheet and surfaced word meanings/readings
/// the profile never opted into.
/// </summary>
[TestClass]
public sealed class EpisodePlayerToolsGatingTests
{
    [TestMethod]
    public void PlayerToolsAloneDoesNotShowTheFallbackLearningSheet()
    {
        var model = NewModel();
        SetLearningSettings(model, LearningMode.Custom, [LearningCapability.PlayerTools]);

        Assert.IsFalse(
            model.ShowPlayerTools,
            "PlayerTools without Lookup/ReadingAids/AiExplanations has nothing to show.");
    }

    [TestMethod]
    public void PlayerToolsWithLookupShowsTheFallbackLearningSheet()
    {
        var model = NewModel();
        SetLearningSettings(
            model,
            LearningMode.Custom,
            [LearningCapability.PlayerTools, LearningCapability.LanguageLookup]);

        Assert.IsTrue(model.ShowPlayerTools);
    }

    [TestMethod]
    public void PlayerToolsWithReadingAidsShowsTheFallbackLearningSheet()
    {
        var model = NewModel();
        SetLearningSettings(
            model,
            LearningMode.Custom,
            [LearningCapability.PlayerTools, LearningCapability.ReadingAids]);

        Assert.IsTrue(model.ShowPlayerTools);
    }

    [TestMethod]
    public void StudyDefaultsShowTheFallbackLearningSheet()
    {
        var model = NewModel();
        SetLearningSettings(model, LearningMode.Study, LearningConfigurationDefaults
            .For(LearningMode.Study)
            .Where(x => x.Value)
            .Select(x => x.Key));

        Assert.IsTrue(model.ShowPlayerTools, "Study enables PlayerTools and language-tools capabilities by default.");
    }

    [TestMethod]
    public void OffNeverShowsTheFallbackLearningSheet()
    {
        var model = NewModel();
        SetLearningSettings(model, LearningMode.Off, []);

        Assert.IsFalse(model.ShowPlayerTools);
    }

    private static EpisodeModel NewModel() =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static void SetLearningSettings(
        EpisodeModel model,
        LearningMode mode,
        IEnumerable<LearningCapability> enabled)
    {
        var enabledSet = enabled.ToHashSet();
        var capabilities = Enum.GetValues<LearningCapability>()
            .ToDictionary(capability => capability, enabledSet.Contains);
        var settings = new LearningResolvedSettings(mode, capabilities);

        typeof(EpisodeModel)
            .GetProperty(
                nameof(EpisodeModel.LearningSettings),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(model, settings);
    }
}
