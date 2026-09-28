using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Naming;

/// <summary>Fixed sample data so <c>/Settings/ReadingNaming</c> can preview a template before saving,
/// the same way <c>/Settings/Naming</c> previews anime templates against a fixed sample series.</summary>
public static class ReadingNamingSamples
{
    public static ReadingNamingRequest Request(MediaAcquisitionKind kind) => kind switch
    {
        MediaAcquisitionKind.Book => new ReadingNamingRequest(
            kind,
            Series: "The Sample Novel",
            Author: "Jane Author",
            Title: "The Sample Novel",
            Language: "en",
            Format: "EPUB",
            OriginalFileName: "the-sample-novel"),
        MediaAcquisitionKind.LightNovel => new ReadingNamingRequest(
            kind,
            Series: "Sample Chronicles",
            Author: "Taro Writer",
            VolumeTitle: "Prologue: The Beginning",
            VolumeNumber: 1,
            Language: "en",
            Format: "EPUB",
            OriginalFileName: "Sample_Chronicles_Vol01"),
        MediaAcquisitionKind.Manga => new ReadingNamingRequest(
            kind,
            Series: "Sample Manga",
            VolumeNumber: 2,
            ChapterNumber: 12.5,
            Format: "CBZ",
            OriginalFileName: "Sample Manga c012.5"),
        _ => new ReadingNamingRequest(kind, Series: "Sample")
    };

    public static IReadOnlyList<ReadingNamingPreviewLine> Preview(ReadingNamingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var request = Request(profile.MediaKind);
        return
        [
            new("Series/work folder", ReadingNamingFormatter.BuildSeriesFolderName(profile, request)),
            new("File name", ReadingNamingFormatter.BuildFileName(profile, request) + "." + (request.Format?.ToLowerInvariant() ?? "epub"))
        ];
    }
}
