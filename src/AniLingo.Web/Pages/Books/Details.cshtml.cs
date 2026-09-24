using AniLingo.Web.Features.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Books;

public sealed class DetailsModel(
    BookCatalogService books,
    IBookTranslator translator) : PageModel
{
    public BookCatalogItem? Book { get; private set; }
    public string? SourceText { get; private set; }
    public string? TranslatedText { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyList<string> OriginalParagraphs =>
        SplitParagraphs(SourceText);

    public IReadOnlyList<string> TranslatedParagraphs =>
        SplitParagraphs(TranslatedText);

    public async Task<IActionResult> OnGetAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostTranslateAsync(
        int id,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(SourceText))
        {
            Error = "No readable English text is available for this book.";
            return Page();
        }

        try
        {
            TranslatedText = await translator.TranslateEnglishAsync(
                SourceText,
                "Indonesian",
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
        }

        return Page();
    }

    private async Task<bool> LoadAsync(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            Book = await books.GetAsync(id, cancellationToken);
            if (Book is null)
            {
                return false;
            }

            SourceText = await books.GetReadableSampleAsync(
                Book,
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            Error = exception.Message;
            return Book is not null;
        }
    }

    private static IReadOnlyList<string> SplitParagraphs(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split(
                    "\n\n",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
