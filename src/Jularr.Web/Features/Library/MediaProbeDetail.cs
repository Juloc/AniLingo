using System.Globalization;
using System.Text.Json;

namespace Jularr.Web.Features.Library;

public enum MediaProbeStreamType
{
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
    Unknown
}

// One ffprobe stream with every property a preservation or compatibility decision needs,
// including the attachment, data and cover-art streams the persisted inventory leaves out.
public sealed record MediaProbeStream
{
    public required int Index { get; init; }
    public required MediaProbeStreamType Type { get; init; }
    public string? Codec { get; init; }
    public string? CodecTag { get; init; }
    public string? Profile { get; init; }
    public int? Level { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? PixelFormat { get; init; }
    public int? BitDepth { get; init; }
    public string? FrameRate { get; init; }
    public string? ColorPrimaries { get; init; }
    public string? ColorTransfer { get; init; }
    public string? ColorSpace { get; init; }
    public string? DynamicRange { get; init; }
    public int? Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public int? SampleRate { get; init; }
    public string? Language { get; init; }
    public string? Title { get; init; }
    public string? HandlerName { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }

    // nb_frames (MP4) or the muxer statistics tag NUMBER_OF_FRAMES (Matroska).
    public long? FrameCount { get; init; }

    public IReadOnlySet<string> Dispositions { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlyList<string> SideDataTypes { get; init; } = [];

    public bool IsAttachedPicture => Dispositions.Contains("attached_pic");
    public bool IsDefault => Dispositions.Contains("default");

    // MP4 stores a track name as the handler name; Matroska as the title tag.
    public string? DisplayTitle => Title ?? HandlerName;
}

public sealed record MediaProbeChapter(double StartSeconds, double EndSeconds, string? Title);

// The complete `ffprobe -show_format -show_streams -show_chapters` view of one file. The
// persisted inventory (MediaTechnicalInfo) is its playback summary; this detail is read on
// demand where every stream, chapter and attachment matters.
public sealed record MediaProbeDetail(
    string? FormatName,
    double? DurationSeconds,
    string? Title,
    IReadOnlyList<MediaProbeStream> Streams,
    IReadOnlyList<MediaProbeChapter> Chapters)
{
    public MediaProbeStream? PrimaryVideo =>
        Streams.FirstOrDefault(x => x.Type == MediaProbeStreamType.Video && !x.IsAttachedPicture);

    public IEnumerable<MediaProbeStream> OfType(MediaProbeStreamType type) =>
        Streams.Where(x => x.Type == type);

    // The audio stream a player starts without a selection: the first flagged default, else the first.
    public MediaProbeStream? DefaultAudio =>
        OfType(MediaProbeStreamType.Audio).FirstOrDefault(x => x.IsDefault) ??
        OfType(MediaProbeStreamType.Audio).FirstOrDefault();
}

public static partial class MediaProbeParser
{
    public static MediaProbeDetail ParseDetail(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("ffprobe output is not a JSON object.");
        }

        string? formatName = null;
        string? title = null;
        double? durationSeconds = null;
        if (root.TryGetProperty("format", out var format) &&
            format.ValueKind == JsonValueKind.Object)
        {
            formatName = ReadString(format, "format_name");
            durationSeconds = ReadPositiveDouble(format, "duration");
            if (format.TryGetProperty("tags", out var formatTags) &&
                formatTags.ValueKind == JsonValueKind.Object)
            {
                title = ReadTag(formatTags, "title");
            }
        }

        var streams = new List<MediaProbeStream>();
        if (root.TryGetProperty("streams", out var streamArray) &&
            streamArray.ValueKind == JsonValueKind.Array)
        {
            var position = 0;
            foreach (var stream in streamArray.EnumerateArray())
            {
                streams.Add(ParseDetailStream(stream, ReadInt(stream, "index") ?? position));
                position++;
            }
        }

        var chapters = new List<MediaProbeChapter>();
        if (root.TryGetProperty("chapters", out var chapterArray) &&
            chapterArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var chapter in chapterArray.EnumerateArray())
            {
                string? chapterTitle = null;
                if (chapter.TryGetProperty("tags", out var chapterTags) &&
                    chapterTags.ValueKind == JsonValueKind.Object)
                {
                    chapterTitle = ReadTag(chapterTags, "title");
                }

                chapters.Add(new MediaProbeChapter(
                    ReadDouble(chapter, "start_time") ?? 0,
                    ReadDouble(chapter, "end_time") ?? 0,
                    chapterTitle));
            }
        }

        return new MediaProbeDetail(
            formatName,
            durationSeconds,
            title,
            [.. streams.OrderBy(x => x.Index)],
            chapters);
    }

    private static MediaProbeStream ParseDetailStream(JsonElement stream, int index)
    {
        var type = ReadString(stream, "codec_type")?.ToLowerInvariant() switch
        {
            "video" => MediaProbeStreamType.Video,
            "audio" => MediaProbeStreamType.Audio,
            "subtitle" => MediaProbeStreamType.Subtitle,
            "attachment" => MediaProbeStreamType.Attachment,
            "data" => MediaProbeStreamType.Data,
            _ => MediaProbeStreamType.Unknown
        };

        JsonElement? tags = stream.TryGetProperty("tags", out var tagElement) &&
                            tagElement.ValueKind == JsonValueKind.Object
            ? tagElement
            : null;

        var sideDataTypes = ReadSideDataTypes(stream);
        var pixelFormat = ReadString(stream, "pix_fmt");
        string? dynamicRange = null;
        if (type == MediaProbeStreamType.Video)
        {
            dynamicRange = ReadDynamicRange(stream);
            if (dynamicRange == "HDR10" && sideDataTypes.Any(IsHdr10PlusSideData))
            {
                dynamicRange = "HDR10+";
            }
        }

        return new MediaProbeStream
        {
            Index = index,
            Type = type,
            Codec = ReadString(stream, "codec_name"),
            CodecTag = ReadString(stream, "codec_tag_string"),
            Profile = ReadString(stream, "profile"),
            Level = ReadInt(stream, "level"),
            Width = ReadInt(stream, "width"),
            Height = ReadInt(stream, "height"),
            PixelFormat = pixelFormat,
            BitDepth = type == MediaProbeStreamType.Video ? ReadBitDepth(stream, pixelFormat) : null,
            FrameRate = type == MediaProbeStreamType.Video ? ReadString(stream, "avg_frame_rate") : null,
            ColorPrimaries = ReadString(stream, "color_primaries"),
            ColorTransfer = ReadString(stream, "color_transfer"),
            ColorSpace = ReadString(stream, "color_space"),
            DynamicRange = dynamicRange,
            Channels = ReadInt(stream, "channels"),
            ChannelLayout = ReadString(stream, "channel_layout"),
            SampleRate = ReadInt(stream, "sample_rate"),
            Language = tags is { } languageTags ? ReadTag(languageTags, "language") : null,
            Title = tags is { } titleTags ? ReadTag(titleTags, "title") : null,
            HandlerName = tags is { } handlerTags ? ReadTag(handlerTags, "handler_name") : null,
            FileName = tags is { } fileTags ? ReadTag(fileTags, "filename") : null,
            MimeType = tags is { } mimeTags ? ReadTag(mimeTags, "mimetype") : null,
            FrameCount = ReadLong(stream, "nb_frames") ?? (tags is { } statisticTags ? ReadFrameCountTag(statisticTags) : null),
            Dispositions = ReadDispositions(stream),
            SideDataTypes = sideDataTypes
        };
    }

    private static bool IsHdr10PlusSideData(string sideDataType) =>
        sideDataType.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ||
        sideDataType.Contains("SMPTE2094-40", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> ReadDispositions(JsonElement stream)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (stream.TryGetProperty("disposition", out var disposition) &&
            disposition.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in disposition.EnumerateObject())
            {
                if ((property.Value.ValueKind == JsonValueKind.Number &&
                     property.Value.TryGetInt32(out var number) &&
                     number != 0) ||
                    property.Value.ValueKind == JsonValueKind.True)
                {
                    flags.Add(property.Name);
                }
            }
        }

        return flags;
    }

    private static List<string> ReadSideDataTypes(JsonElement stream)
    {
        var types = new List<string>();
        if (stream.TryGetProperty("side_data_list", out var sideData) &&
            sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (ReadString(item, "side_data_type") is { } type)
                {
                    types.Add(type);
                }
            }
        }

        return types;
    }

    // mkvmerge writes NUMBER_OF_FRAMES, sometimes with a language suffix (NUMBER_OF_FRAMES-eng).
    private static long? ReadFrameCountTag(JsonElement tags)
    {
        foreach (var property in tags.EnumerateObject())
        {
            if (property.Name.StartsWith("NUMBER_OF_FRAMES", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                long.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames) &&
                frames >= 0)
            {
                return frames;
            }
        }

        return null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName) =>
        ReadString(element, propertyName) is { } text &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value)
            ? value
            : null;
}
