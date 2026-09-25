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

        var stateCounts = await LearningQueries.WordCards(db, currentAccount.ProfileId)
            .AsNoTracking()
            .GroupBy(x => x.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.State, x => x.Count, cancellationToken);

        LearningTerms = stateCounts.GetValueOrDefault(UserTermState.Learning);
        SavedTerms = stateCounts.GetValueOrDefault(UserTermState.Saved);
        KnownTerms = stateCounts.GetValueOrDefault(UserTermState.Known);
        IgnoredTerms = stateCounts.GetValueOrDefault(UserTermState.Ignored);

        DueReviews = await LearningQueries
            .DueCards(db, currentAccount.ProfileId, DateTime.UtcNow)
            .CountAsync(cancellationToken);

        var hasVocabularyState =
            LearningTerms + SavedTerms + KnownTerms + IgnoredTerms > 0;

        ShowReviews =
            resolved.IsEnabled(LearningCapability.Reviews)
            || LearningTerms > 0;
        ShowVocabulary =
            resolved.IsEnabled(LearningCapability.Vocabulary)
            || hasVocabularyState;
        ShowSentences =
            resolved.IsEnabled(LearningCapability.SentencePractice);
        ShowKana =
            resolved.IsEnabled(LearningCapability.ScriptTrainer);
        ShowProgress =
            resolved.IsEnabled(LearningCapability.Progress)
            || hasVocabularyState;
    }
}
