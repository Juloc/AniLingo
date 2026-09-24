namespace AniLingo.Web.Features.Books;

public interface IBookTranslator
{
    string Id { get; }

    Task<string> TranslateEnglishAsync(
        string englishText,
        string targetLanguage,
        CancellationToken cancellationToken);
}
