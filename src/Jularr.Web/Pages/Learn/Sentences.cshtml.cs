using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Learning.LanguageAssistance;
using Jularr.Web.Features.Learning.Sentences;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Learn;

public sealed class SentencesModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    LanguageTextAnalyzer analyzer,
    AiSentenceExplanationService explanations) : PageModel
{
    public const int PageSize = 12;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public SentencePracticeMode Mode { get; private set; } = SentencePracticeMode.Cloze;
    public IReadOnlyList<SentencePracticeItem> Sentences { get; private set; } = [];

    /// <summary>Tokens open the shared language inspector (lookup or readings on).</summary>
    public bool ShowInspector { get; private set; }

    /// <summary>Cached AI explanations are shown after revealing a sentence.</summary>
    public bool ShowExplanations { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? mode,
        CancellationToken cancellationToken)
    {
        var resolved = await LearningModuleGate.ResolveAsync(
            db,
            currentAccount.ProfileId,
            cancellationToken);
        if (!resolved.Sentences)
        {
            return LearningModuleGate.RedirectToHub();
        }

        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        var assistance = LanguageAssistanceAvailability.From(resolved.Settings, surface: null);
        ShowInspector = assistance.Any;
        ShowExplanations = assistance.Explanations;
        Mode = SentencePracticeModes.Parse(mode);

        Sentences = await new SentencePracticeService(db, analyzer, explanations).LoadAsync(
            currentAccount.ProfileId,
            Mode,
            PageSize,
            withCachedExplanations: ShowExplanations,
            cancellationToken);
        return Page();
    }
}
