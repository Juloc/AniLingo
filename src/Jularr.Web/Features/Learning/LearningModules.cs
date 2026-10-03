using Jularr.Web.Data;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Learning;

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

public sealed class LearningModuleResolver(
    AppDbContext db,
    IInstanceModuleService? instanceModules = null)
{
    public async Task<LearningModuleAvailability> ResolveAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return new LearningModuleAvailability(
                DisabledSettings,
                Reviews: false,
                Vocabulary: false,
                Sentences: false,
                Kana: false,
                Progress: false);
        }

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

    /// <summary>
    /// Language assistance for one scope: the Learning Hub (no scope, profile
    /// settings) or a content scope resolved through the full profile → media
    /// type → work → content hierarchy. <paramref name="surface"/> names the
    /// player/reader that hosts the inspector so PlayerTools/ReaderTools apply.
    /// </summary>
    public async Task<LanguageAssistance.LanguageAssistanceAvailability> ResolveAssistanceAsync(
        string profileId,
        LearningScopeContext? scope,
        LanguageAssistance.LanguageSourceType? surface,
        CancellationToken cancellationToken)
    {
        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return LanguageAssistance.LanguageAssistanceAvailability.None;
        }

        var store = new LearningConfigurationStore(db);
        var settings = scope is null
            ? await store.ResolveProfileAsync(profileId, cancellationToken)
            : await store.ResolveAsync(profileId, scope, cancellationToken);

        return LanguageAssistance.LanguageAssistanceAvailability.From(settings, surface);
    }

    /// <summary>
    /// Resolves the Translation capability for a Novel/Book work, optionally
    /// narrowed to one chapter, through the canonical profile → media type →
    /// work → content hierarchy. This only authorizes *generating* (or
    /// regenerating) a translation - the readers' translate handlers and the
    /// library's whole-book translate/regenerate actions. It must never gate
    /// reading an already cached translation (the "translated" badges and the
    /// cached text itself): that is core reader behaviour and must work with
    /// Learning off (#369).
    /// </summary>
    public async Task<bool> ResolveTranslationEnabledAsync(
        string profileId,
        LearningMediaType mediaType,
        string workKey,
        string? contentKey,
        CancellationToken cancellationToken)
    {
        if (!await IsLearningEnabledAsync(cancellationToken))
        {
            return false;
        }

        var settings = await new LearningConfigurationStore(db).ResolveAsync(
            profileId,
            new LearningScopeContext(mediaType, workKey, contentKey),
            cancellationToken);

        return settings.IsEnabled(LearningCapability.Translation);
    }

    private async Task<bool> IsLearningEnabledAsync(CancellationToken cancellationToken) =>
        instanceModules is null
        || await instanceModules.IsEnabledAsync(
            InstanceModule.Learning,
            cancellationToken);

    private static LearningResolvedSettings DisabledSettings { get; } =
        new(
            LearningMode.Off,
            Enum.GetValues<LearningCapability>()
                .ToDictionary(capability => capability, _ => false));

    /// <summary>The Kana trainer is the writing-system trainer of the Japanese toolkit.</summary>
    public const string KanaLanguageTag = "ja";

    private async Task<bool> HasScriptTrainerCourseAsync(
        string profileId,
        string languageTag,
        CancellationToken cancellationToken)
    {
        if (!LearningLanguageToolkitRegistry.Supports(languageTag, LearningLanguageCapability.ScriptTrainer))
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
