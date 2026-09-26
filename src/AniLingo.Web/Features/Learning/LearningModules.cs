using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

/// <summary>
/// Learning Hub modules visible for a profile. Every flag comes from the
/// canonical <see cref="LearningConfigurationStore"/> resolver at profile scope;
/// historic cards never re-surface a module the profile switched off.
/// </summary>
public sealed record LearningModuleAvailability(
    LearningResolvedSettings Settings,
    bool Reviews,
    bool Vocabulary,
    bool Sentences,
    bool Kana,
    bool Progress)
{
    public bool AnyModule => Reviews || Vocabulary || Sentences || Kana || Progress;

    /// <summary>
    /// Language tools are on but no study module is: lookup, readings and
    /// translation work without cards or spaced repetition.
    /// </summary>
    public bool LanguageToolsOnly =>
        !AnyModule
        && (Settings.IsEnabled(LearningCapability.LanguageLookup)
            || Settings.IsEnabled(LearningCapability.ReadingAids)
            || Settings.IsEnabled(LearningCapability.Translation)
            || Settings.IsEnabled(LearningCapability.AiExplanations)
            || Settings.IsEnabled(LearningCapability.PlayerTools)
            || Settings.IsEnabled(LearningCapability.ReaderTools));

    /// <summary>
    /// The script trainer capability is on, but no enabled course studies a
    /// language whose toolkit provides one.
    /// </summary>
    public bool ScriptTrainerNeedsCourse =>
        Settings.IsEnabled(LearningCapability.ScriptTrainer) && !Kana;
}

public sealed class LearningModuleResolver(AppDbContext db)
{
    private static readonly LearningLanguageToolkitRegistry Toolkits = new();

    public async Task<LearningModuleAvailability> ResolveAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var settings = await new LearningConfigurationStore(db).ResolveProfileAsync(
            profileId,
            cancellationToken);

        var kana = settings.IsEnabled(LearningCapability.ScriptTrainer)
            && await HasScriptTrainerCourseAsync(
                profileId,
                KanaLanguageTag,
                cancellationToken);

        return new LearningModuleAvailability(
            settings,
            Reviews: settings.IsEnabled(LearningCapability.Reviews),
            Vocabulary: settings.IsEnabled(LearningCapability.Vocabulary),
            Sentences: settings.IsEnabled(LearningCapability.SentencePractice),
            Kana: kana,
            Progress: settings.IsEnabled(LearningCapability.Progress));
    }

    /// <summary>The Kana trainer is the writing-system trainer of the Japanese toolkit.</summary>
    public const string KanaLanguageTag = "ja";

    private async Task<bool> HasScriptTrainerCourseAsync(
        string profileId,
        string languageTag,
        CancellationToken cancellationToken)
    {
        if (!Toolkits.Get(languageTag).Supports(LearningLanguageCapability.ScriptTrainer))
        {
            return false;
        }

        return await db.LearningCourses
            .AsNoTracking()
            .AnyAsync(
                x => x.ProfileId == profileId
                    && x.IsEnabled
                    && x.SourceLanguage == languageTag,
                cancellationToken);
    }
}
