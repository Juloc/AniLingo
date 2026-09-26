using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Learn;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public LearningMode Mode { get; private set; } = LearningMode.Off;

    /// <summary>
    /// True when any scope of the profile enables Learning, even if the
    /// profile-level modules below stay hidden.
    /// </summary>
    public bool LearningEnabled { get; private set; }

    public int DueReviews { get; private set; }
    public int LearningTerms { get; private set; }
    public int SavedTerms { get; private set; }
    public int KnownTerms { get; private set; }
    public int IgnoredTerms { get; private set; }

    public bool ShowReviews { get; private set; }
    public bool ShowVocabulary { get; private set; }
    public bool ShowSentences { get; private set; }
    public bool ShowKana { get; private set; }
    public bool ShowProgress { get; private set; }

    public bool ShowAnyModule { get; private set; }
    public bool ShowMetrics => ShowReviews || ShowVocabulary;

    /// <summary>
    /// Language tools are on for the profile but no study module is; the hub
    /// explains that instead of showing empty modules.
    /// </summary>
    public bool LanguageToolsOnly { get; private set; }

    /// <summary>
    /// The writing-system trainer is enabled, but no enabled course studies a
    /// language that has one (Kana needs a Japanese course).
    /// </summary>
    public bool ScriptTrainerNeedsCourse { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        var modules = await new LearningModuleResolver(db).ResolveAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Mode = modules.Settings.Mode;
        LearningEnabled = await new LearningConfigurationStore(db).HasAnyLearningEnabledAsync(
            currentAccount.ProfileId,
            cancellationToken);

        ShowReviews = modules.Reviews;
        ShowVocabulary = modules.Vocabulary;
        ShowSentences = modules.Sentences;
        ShowKana = modules.Kana;
        ShowProgress = modules.Progress;
        ShowAnyModule = modules.AnyModule;
        LanguageToolsOnly = modules.LanguageToolsOnly;
        ScriptTrainerNeedsCourse = modules.ScriptTrainerNeedsCourse;

        if (ShowVocabulary)
        {
            var stateCounts = await LearningQueries.WordCards(db, currentAccount.ProfileId)
                .AsNoTracking()
                .GroupBy(x => x.State)
                .Select(group => new { State = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.State, x => x.Count, cancellationToken);

            LearningTerms = stateCounts.GetValueOrDefault(UserTermState.Learning);
            SavedTerms = stateCounts.GetValueOrDefault(UserTermState.Saved);
            KnownTerms = stateCounts.GetValueOrDefault(UserTermState.Known);
            IgnoredTerms = stateCounts.GetValueOrDefault(UserTermState.Ignored);
        }

        if (ShowReviews)
        {
            DueReviews = await LearningQueries
                .DueCards(db, currentAccount.ProfileId, DateTime.UtcNow)
                .CountAsync(cancellationToken);
        }
    }
}
