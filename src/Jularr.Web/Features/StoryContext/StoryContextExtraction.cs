namespace Jularr.Web.Features.StoryContext;

public sealed record StoryChapterExtractionRequest(
    int ChapterNumber,
    string ChapterTitle,
    string SourceLanguage,
    string SourceText,
    string ExistingContext);

public sealed record StoryExtractedEntity(
    string Name,
    string Type,
    IReadOnlyList<string>? Aliases,
    string? Description,
    string? Pronouns,
    string? Relationships,
    string? Appearance);

public sealed record StoryExtractedTerm(
    string Source,
    string Category);

public sealed record StoryChapterExtraction(
    string? ChapterSummary,
    string? ContinuityNotes,
    IReadOnlyList<StoryExtractedEntity> Entities,
    IReadOnlyList<StoryExtractedTerm> Terms)
{
    public static StoryChapterExtraction Empty { get; } = new(null, null, [], []);
}

/// <summary>
/// Language-neutral chapter extraction for consumers that need story memory
/// without a translation (for example chapter artwork of an untranslated
/// book). Translation keeps using its own combined extraction, whose neutral
/// part lands in the same shared memory, so a chapter is extracted once.
/// </summary>
public interface IStoryContextExtractor
{
    string Id { get; }

    Task<StoryChapterExtraction> ExtractChapterAsync(
        StoryChapterExtractionRequest request,
        CancellationToken cancellationToken);
}
