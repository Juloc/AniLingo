namespace AniLingo.Web.Features.Books;

public interface IBookTranslator
{
    string Id { get; }

    Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken);

    Task<string> TranslateEnglishAsync(
        string englishText,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        TranslateLiteraryAsync(
            englishText,
            "en",
            targetLanguage,
            context: "",
            cancellationToken);
}
