namespace Jularr.Web.Features.Audiobooks;

/// <summary>
/// The audio file formats Jularr imports as audiobooks (#440). M4B is the canonical chaptered
/// container; M4A and plain MP3 (single file or one part per chapter) are accepted as they are and
/// never transcoded. The stored <see cref="AudiobookFile.Format"/> is the upper-case token here.
/// </summary>
public static class AudiobookFileFormats
{
    public const string M4b = "M4B";
    public const string M4a = "M4A";
    public const string Mp3 = "MP3";
    public const string Ogg = "OGG";
    public const string Opus = "OPUS";
    public const string Flac = "FLAC";

    public static string? FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".m4b" => M4b,
            ".m4a" => M4a,
            ".mp3" => Mp3,
            ".ogg" => Ogg,
            ".opus" => Opus,
            ".flac" => Flac,
            _ => null
        };

    public static bool IsSupported(string path) => FromPath(path) is not null;
}
