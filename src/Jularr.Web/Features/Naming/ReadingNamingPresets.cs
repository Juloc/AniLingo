using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Naming;

/// <summary>
/// Default reading naming profiles. The default profile for every media type keeps the series/
/// work folder named after the title and every file under its original name - byte-identical to
/// the placement Jularr already did before naming profiles existed (#389's
/// <c>MangaLibraryPlacement</c>/<c>ReadingLibraryPlacement</c>), so nothing breaks for existing
/// setups. "Structured" is an example a owner can switch to without writing a template from
/// scratch, mirroring anime naming's presets.
/// </summary>
public static class ReadingNamingPresets
{
    public static ReadingNamingProfile Default(MediaAcquisitionKind kind) =>
        new(
            kind,
            "Original names",
            SeriesFolderFormat: "{Series}",
            FileFormat: "{Original Title}");

    public static ReadingNamingProfile Structured(MediaAcquisitionKind kind) =>
        kind switch
        {
            MediaAcquisitionKind.Book => new ReadingNamingProfile(
                kind,
                "Author, title",
                SeriesFolderFormat: "{Author}",
                FileFormat: "{Series} - {Title}"),
            MediaAcquisitionKind.LightNovel => new ReadingNamingProfile(
                kind,
                "Series, volume",
                SeriesFolderFormat: "{Series}",
                FileFormat: "{Series} - Volume {Volume Number:00} - {Volume Title}"),
            MediaAcquisitionKind.Manga => new ReadingNamingProfile(
                kind,
                "Series, chapter",
                SeriesFolderFormat: "{Series}",
                FileFormat: "{Series} - Chapter {Chapter Number:000}"),
            _ => Default(kind)
        };

    public static IReadOnlyList<MediaAcquisitionKind> ReadingKinds { get; } =
        [MediaAcquisitionKind.Book, MediaAcquisitionKind.Manga, MediaAcquisitionKind.LightNovel];

    public static IReadOnlyList<ReadingNamingProfile> PresetsFor(MediaAcquisitionKind kind) =>
        [Default(kind), Structured(kind)];

    public static ReadingNamingState CreateDefaultState() =>
        new(
            Version: 1,
            Profiles: ReadingKinds.ToDictionary(kind => kind.ToString(), Default, StringComparer.OrdinalIgnoreCase));
}
