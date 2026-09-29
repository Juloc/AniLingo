namespace Jularr.Web.Features.Audiobooks;

/// <summary>
/// An audiobook as a first-class media type (#440). An audiobook is the audio edition of a book: its
/// universal identity, titles and external provider ids live in the media core as a
/// <see cref="Jularr.Web.Features.MediaCore.Work"/> of media type Book, and the audiobook release is
/// modelled there as a <c>WorkEdition</c> (format <c>audiobook</c>) owning a <c>WorkVersion</c>, bridged
/// by <c>WorkSourceKind.Audiobook</c>. This per-type row is the stable anchor the audiobook library and
/// per-user progress build on; it is never modified by a metadata refresh of the core it bridges to.
/// </summary>
public sealed class Audiobook
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable de-duplication key (folded title + year) so a re-import resolves the same audiobook.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Publication year when known; part of <see cref="Key"/> so re-issues stay distinct.</summary>
    public int? Year { get; set; }

    /// <summary>The book's author when known.</summary>
    public string? Author { get; set; }

    /// <summary>The narrator/reader of this audiobook when known (an audiobook-specific credit).</summary>
    public string? Narrator { get; set; }

    /// <summary>Audible ASIN when known; the primary external identity is also mirrored into the media core.</summary>
    public string? Asin { get; set; }

    /// <summary>Total spoken duration in milliseconds when known (summed across the audiobook's files).</summary>
    public long? DurationMs { get; set; }

    /// <summary>Number of chapters when known (embedded chapter markers, or one per file for chaptered MP3s).</summary>
    public int? ChapterCount { get; set; }

    /// <summary>The library folder the audiobook's files were placed in; null when imported in place.</summary>
    public string? LibraryPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One audio file of an <see cref="Audiobook"/> — a single M4B/M4A container, or one of the MP3 parts of
/// a chaptered audiobook. Kept as a per-type record (not a media-core structure row) because it is the
/// concrete local artifact the future player streams. Idempotent per audiobook on <see cref="FileKey"/>
/// so a re-import refreshes the row instead of duplicating it.
/// </summary>
public sealed class AudiobookFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudiobookId { get; set; }

    /// <summary>Deterministic identity of the file within the audiobook (folded file name).</summary>
    public string FileKey { get; set; } = "";

    public string FileName { get; set; } = "";

    /// <summary>Upper-case container format, e.g. <c>M4B</c>, <c>M4A</c>, <c>MP3</c> (see <see cref="AudiobookFileFormats"/>).</summary>
    public string Format { get; set; } = "";

    /// <summary>Absolute path of the file on disk.</summary>
    public string StoragePath { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>Spoken duration of this file in milliseconds when known.</summary>
    public long? DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
