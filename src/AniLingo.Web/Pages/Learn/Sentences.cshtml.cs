using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Learn;

public sealed class SentencesModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    IJapaneseMorphology morphology,
    JapaneseDictionary dictionary) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<SentencePracticeItem> Sentences { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
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

        var service = new SentencePracticeService(
            db,
            currentAccount.ProfileId,
            morphology,
            dictionary);

        Sentences = await service.LoadAsync(
            12,
            cancellationToken);
        return Page();
    }
}
