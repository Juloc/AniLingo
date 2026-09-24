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
        string id,
        CancellationToken cancellationToken)
    {
        return await LoadAsync(id, cancellationToken)
            ? Page()
            : NotFound();
    }

    public async Task<IActionResult> OnPostTranslateAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(SourceText))
        {
            Error = "No readable text source is available for this book yet.";
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
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            Book = await books.GetAsync(id, cancellationToken);
            if (Book is null)
            {
                return false;
            }

            if (Book.CanRead)
            {
                SourceText = await books.GetReadableSampleAsync(
                    Book,
                    cancellationToken);
            }

            return true;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Error = "The book source timed out. Please try again.";
            return Book is not null;
        }
        catch (HttpRequestException)
        {
            Error = "The book source is temporarily unavailable. Please try again.";
            return Book is not null;
        }
        catch (InvalidOperationException exception)
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
