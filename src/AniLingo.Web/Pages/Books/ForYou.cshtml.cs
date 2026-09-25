using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class ForYouModel(
    BookCatalogService books,
    AppDbContext db,
    CurrentAccountContext account) : PageModel
{
    public string TargetLanguage { get; private set; } = "id";
    public BookRecommendationResult Recommendations { get; private set; } =
        BookRecommendationResult.Empty;

    public async Task OnGetAsync(
        string? lang,
        CancellationToken cancellationToken)
    {
        TargetLanguage = BookLanguageCatalog.Normalize(lang);

        Recommendations = await new BookRecommendationService(books, db)
            .GetForProfileAsync(
                account.ProfileId,
                TargetLanguage,
                cancellationToken);
    }
}
