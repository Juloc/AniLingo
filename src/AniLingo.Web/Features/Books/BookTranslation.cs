namespace AniLingo.Web.Features.Books;

public sealed record BookTranslationAnalysisRequest(
    string Title,
    string? Author,
    string? Description,
    IReadOnlyList<string> Genres,
    string SourceLanguage,
    string TargetLanguage,
    string SourceSample);

public sealed record BookLiteraryEditRequest(
    string SourceText,
    string DraftTranslation,
    string SourceLanguage,
    string TargetLanguage,
    string Context);

public sealed record BookTranslationQaRequest(
    string SourceText,
    string EditedTranslation,
    string SourceLanguage,
    string TargetLanguage,
    string Context);

public sealed record BookTranslationMemoryRequest(
    int ChapterNumber,
    string ChapterTitle,
    string SourceText,
    string FinalTranslation,
    string SourceLanguage,
    string TargetLanguage,
    string ExistingContext);

public sealed record BookTranslationEntity(
    string SourceName,
    string TargetName,
    string Type,
    string? Description,
    string? Pronouns,
    string? Relationships,
    string? VoiceNotes);

public sealed record BookTranslationTerm(
    string Source,
    string Target,
    string Category,
    string? Notes,
    bool Locked = false);

public sealed record BookTranslationBibleSeed(
    string? NarrativePerspective,
    string? OverallStyle,
    string? Register,
    string? Audience,
    IReadOnlyList<string> Themes,
    IReadOnlyList<BookTranslationEntity> Entities,
    IReadOnlyList<BookTranslationTerm> Terms)
{
    public static BookTranslationBibleSeed Empty { get; } =
        new(
            null,
            null,
            null,
            null,
            [],
            [],
            []);
}

public sealed record BookTranslationQualityReview(
    bool Accepted,
    string? CorrectedTranslation,
    IReadOnlyList<string> Issues)
{
    public static BookTranslationQualityReview Accept { get; } =
        new(
            true,
            null,
            []);
}

public sealed record BookTranslationMemoryDelta(
    string? ChapterSummary,
    string? ContinuityNotes,
    IReadOnlyList<BookTranslationEntity> Entities,
    IReadOnlyList<BookTranslationTerm> Terms)
{
    public static BookTranslationMemoryDelta Empty { get; } =
        new(
            null,
            null,
            [],
            []);
}

public interface IBookTranslator
{
    string Id { get; }

    Task<string> TranslateLiteraryAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        string context,
        CancellationToken cancellationToken);

    Task<BookTranslationBibleSeed> AnalyzeBookAsync(
        BookTranslationAnalysisRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BookTranslationBibleSeed.Empty);

    Task<string> EditLiteraryAsync(
        BookLiteraryEditRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(request.DraftTranslation);

    Task<BookTranslationQualityReview> ReviewLiteraryAsync(
        BookTranslationQaRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BookTranslationQualityReview.Accept);

    Task<BookTranslationMemoryDelta> ExtractTranslationMemoryAsync(
        BookTranslationMemoryRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(BookTranslationMemoryDelta.Empty);

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
