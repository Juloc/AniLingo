using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Books;

public sealed class ForYouModel(
    BookCatalogService books,
    AppDbContext db,
    CurrentAccountContext account) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public string TargetLanguage { get; private set; } = "id";
    public BookRecommendationResult Recommendations { get; private set; } =
        BookRecommendationResult.Empty;

    public async Task OnGetAsync(
        string? lang,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        TargetLanguage = BookLanguageCatalog.Normalize(lang);

        Recommendations = await new BookRecommendationService(books, db)
            .GetForProfileAsync(
                account.ProfileId,
                TargetLanguage,
                cancellationToken);
    }
}
