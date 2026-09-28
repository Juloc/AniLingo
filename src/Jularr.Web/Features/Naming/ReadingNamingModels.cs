using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Naming;

public enum ReadingNamingScope
{
    SeriesFolder,
    File
}

/// <summary>
/// One naming template pair for a reading media type (Books, Manga or Light Novels): a series/
/// work folder format and a volume/chapter file format. Unlike anime naming there is exactly one
/// profile per media type (#389 gives each reading type a single NAS library root, so there is no
/// per-library-root or per-work assignment to model).
/// </summary>
public sealed record ReadingNamingProfile(
    MediaAcquisitionKind MediaKind,
    string Name,
    string SeriesFolderFormat,
    string FileFormat);

// Canonical persisted reading naming configuration, one profile per media kind. Version follows
// AnimeNamingState's convention so a future breaking format change can be migrated explicitly.
public sealed record ReadingNamingState(int Version, Dictionary<string, ReadingNamingProfile> Profiles);

/// <summary>
/// What is known about a reading release when it is named, at the point the import pipeline
/// places it into the media type's NAS library root (#389). Not every field is available then:
/// Manga has no durable series/chapter entity yet (tracked as a possible follow-up in #529) so its
/// Author/Language are always empty, and an unmatched download only has what its release/file name
/// carries. Unresolved tokens render as empty text, exactly like anime naming.
/// </summary>
public sealed record ReadingNamingRequest(
    MediaAcquisitionKind MediaKind,
    string? Series = null,
    string? Author = null,
    string? Title = null,
    string? VolumeTitle = null,
    int? VolumeNumber = null,
    double? ChapterNumber = null,
    string? Language = null,
    string? Format = null,
    string? OriginalFileName = null);

public sealed record ReadingNamingPreviewLine(string Label, string Value);
