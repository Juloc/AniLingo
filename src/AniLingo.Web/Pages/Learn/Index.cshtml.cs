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

    public bool ShowAnyModule =>
        ShowReviews || ShowVocabulary || ShowSentences || ShowKana || ShowProgress;

    public bool ShowMetrics => ShowReviews || ShowVocabulary;

    /// <summary>
    /// Language tools are on for the profile but no study module is; the hub
    /// explains that instead of showing empty modules.
    /// </summary>
    public bool LanguageToolsOnly { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        var configuration = new LearningConfigurationStore(db);
        var resolved = await configuration.ResolveProfileAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Mode = resolved.Mode;
        LearningEnabled = await configuration.HasAnyLearningEnabledAsync(
            currentAccount.ProfileId,
            cancellationToken);

        // Module visibility comes only from the canonical resolver. Historic
        // vocabulary rows never re-surface a module the profile switched off.
        ShowReviews = resolved.IsEnabled(LearningCapability.Reviews);
        ShowVocabulary = resolved.IsEnabled(LearningCapability.Vocabulary);
        ShowSentences = resolved.IsEnabled(LearningCapability.SentencePractice);
        // ScriptTrainer is the canonical switch; the Kana module additionally
        // assumes the profile studies Japanese until the universal course
        // toolkit (#252) exposes a per-language writing-system trainer.
        ShowKana = resolved.IsEnabled(LearningCapability.ScriptTrainer);
        ShowProgress = resolved.IsEnabled(LearningCapability.Progress);

        LanguageToolsOnly =
            !ShowAnyModule
            && (resolved.IsEnabled(LearningCapability.LanguageLookup)
                || resolved.IsEnabled(LearningCapability.ReadingAids)
                || resolved.IsEnabled(LearningCapability.Translation)
                || resolved.IsEnabled(LearningCapability.AiExplanations)
                || resolved.IsEnabled(LearningCapability.PlayerTools)
                || resolved.IsEnabled(LearningCapability.ReaderTools));

        if (ShowVocabulary)
        {
            var stateCounts = await db.UserTerms
                .AsNoTracking()
                .Where(x => x.ProfileId == currentAccount.ProfileId)
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
            DueReviews = await db.UserTerms
                .AsNoTracking()
                .CountAsync(
                    x => x.ProfileId == currentAccount.ProfileId
                        && x.State == UserTermState.Learning
                        && x.NextReviewAt != null
                        && x.NextReviewAt <= DateTime.UtcNow,
                    cancellationToken);
        }
    }
}
