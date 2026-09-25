namespace AniLingo.Web.Features.Books;

public sealed class BookEdition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkId { get; set; }
    public string EditionKey { get; set; } = "";
    public string Language { get; set; } = "und";
    public string? Isbn10 { get; set; }
    public string? Isbn13 { get; set; }
    public string? Publisher { get; set; }
    public string? PublishedDate { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? SourceProvider { get; set; }
    public string? SourceExternalId { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BookFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EditionId { get; set; }
    public string FileKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "EPUB";
    public string MediaType { get; set; } = "application/epub+zip";
    public string SourceKind { get; set; } = "unknown";
    public string? SourceUrl { get; set; }
    public string ContentHash { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? StoragePath { get; set; }
    public bool IsPrimary { get; set; } = true;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

public sealed record BookEditionFileSnapshot(
    BookEdition Edition,
    IReadOnlyList<BookFile> Files);
