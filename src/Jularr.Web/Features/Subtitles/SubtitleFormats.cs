namespace Jularr.Web.Features.Subtitles;

/// <summary>How a player can show an embedded subtitle stream.</summary>
public enum SubtitleFormatKind
{
    /// <summary>No decoder in the server's ffmpeg (for example teletext or ARIB captions).</summary>
    Unsupported,

    /// <summary>Plain text cues the client draws itself (SRT, WebVTT, mov_text …).</summary>
    Text,

    /// <summary>ASS/SSA: text cues plus styling a client may or may not render.</summary>
    StyledText,

    /// <summary>Bitmaps (PGS, VobSub, DVB, XSUB) that only a native player or a server burn-in can show.</summary>
    Image
}

/// <summary>
/// Classifies ffprobe subtitle codec names. Text formats are extracted to cues through ffmpeg;
/// picture formats are drawn into the video by the server (burn-in) for clients that cannot
/// render them. Anything else is listed but cannot be shown.
/// </summary>
public static class SubtitleFormats
{
    private static readonly Dictionary<string, SubtitleFormatKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["subrip"] = SubtitleFormatKind.Text,
        ["srt"] = SubtitleFormatKind.Text,
        ["webvtt"] = SubtitleFormatKind.Text,
        ["mov_text"] = SubtitleFormatKind.Text,
        ["text"] = SubtitleFormatKind.Text,
        ["microdvd"] = SubtitleFormatKind.Text,
        ["mpl2"] = SubtitleFormatKind.Text,
        ["subviewer"] = SubtitleFormatKind.Text,
        ["subviewer1"] = SubtitleFormatKind.Text,
        ["sami"] = SubtitleFormatKind.Text,
        ["realtext"] = SubtitleFormatKind.Text,
        ["pjs"] = SubtitleFormatKind.Text,
        ["vplayer"] = SubtitleFormatKind.Text,
        ["stl"] = SubtitleFormatKind.Text,
        ["jacosub"] = SubtitleFormatKind.Text,
        ["eia_608"] = SubtitleFormatKind.Text,
        ["ass"] = SubtitleFormatKind.StyledText,
        ["ssa"] = SubtitleFormatKind.StyledText,
        ["hdmv_pgs_subtitle"] = SubtitleFormatKind.Image,
        ["pgssub"] = SubtitleFormatKind.Image,
        ["dvd_subtitle"] = SubtitleFormatKind.Image,
        ["dvdsub"] = SubtitleFormatKind.Image,
        ["dvb_subtitle"] = SubtitleFormatKind.Image,
        ["dvbsub"] = SubtitleFormatKind.Image,
        ["xsub"] = SubtitleFormatKind.Image
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["subrip"] = "SRT",
        ["srt"] = "SRT",
        ["webvtt"] = "WebVTT",
        ["mov_text"] = "MP4 text",
        ["ass"] = "ASS",
        ["ssa"] = "SSA",
        ["hdmv_pgs_subtitle"] = "PGS",
        ["pgssub"] = "PGS",
        ["dvd_subtitle"] = "VobSub",
        ["dvdsub"] = "VobSub",
        ["dvb_subtitle"] = "DVB",
        ["dvbsub"] = "DVB",
        ["xsub"] = "XSUB",
        ["eia_608"] = "CEA-608"
    };

    public static SubtitleFormatKind Classify(string? codec) =>
        codec is not null && Kinds.TryGetValue(codec.Trim(), out var kind)
            ? kind
            : SubtitleFormatKind.Unsupported;

    /// <summary>Text or styled text: ffmpeg can extract the stream as cues.</summary>
    public static bool IsText(string? codec) =>
        Classify(codec) is SubtitleFormatKind.Text or SubtitleFormatKind.StyledText;

    public static bool IsImage(string? codec) => Classify(codec) == SubtitleFormatKind.Image;

    public static bool IsStyled(string? codec) => Classify(codec) == SubtitleFormatKind.StyledText;

    /// <summary>Short technical name for menus and diagnostics ("PGS", "ASS"); the codec id otherwise.</summary>
    public static string? DisplayName(string? codec) =>
        codec is null
            ? null
            : DisplayNames.TryGetValue(codec.Trim(), out var name) ? name : codec.Trim();
}
