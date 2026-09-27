using System.Text.Json.Nodes;

namespace Jularr.Tests;

// ffprobe `-show_format -show_streams -show_chapters -of json` output of typical releases,
// trimmed to the fields Jularr reads.
internal static class MediaOptimizationFixtures
{
    // A web release: H.264 8-bit, two AAC tracks with names, chapters and mkvmerge statistics.
    public const string MkvH264Aac = """
    {
      "streams": [
        {
          "index": 0, "codec_name": "h264", "profile": "High", "codec_type": "video",
          "codec_tag_string": "[0][0][0][0]", "width": 1920, "height": 1080, "pix_fmt": "yuv420p",
          "level": 40, "avg_frame_rate": "24000/1001", "color_primaries": "bt709", "color_transfer": "bt709", "color_space": "bt709",
          "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
          "tags": { "BPS": "5000000", "NUMBER_OF_FRAMES": "34046", "DURATION": "00:23:40.500000000" }
        },
        {
          "index": 1, "codec_name": "aac", "profile": "LC", "codec_type": "audio", "codec_tag_string": "[0][0][0][0]",
          "sample_rate": "48000", "channels": 2, "channel_layout": "stereo",
          "disposition": { "default": 1, "forced": 0 },
          "tags": { "language": "jpn", "title": "Japanese", "NUMBER_OF_FRAMES-eng": "66570" }
        },
        {
          "index": 2, "codec_name": "aac", "profile": "LC", "codec_type": "audio", "codec_tag_string": "[0][0][0][0]",
          "sample_rate": "48000", "channels": 2, "channel_layout": "stereo",
          "disposition": { "default": 0, "forced": 0, "dub": 1 },
          "tags": { "language": "ger", "title": "Deutsch" }
        }
      ],
      "chapters": [
        { "id": 1, "start_time": "0.000000", "end_time": "90.000000", "tags": { "title": "Opening" } },
        { "id": 2, "start_time": "90.000000", "end_time": "1330.000000", "tags": { "title": "Episode" } },
        { "id": 3, "start_time": "1330.000000", "end_time": "1420.500000", "tags": { "title": "Ending" } }
      ],
      "format": {
        "format_name": "matroska,webm", "duration": "1420.500000",
        "tags": { "title": "Frieren - S01E02", "ENCODER": "Lavf60.16.100" }
      }
    }
    """;

    // What `ffmpeg -map 0 -c copy -f mp4` produces for MkvH264Aac: same streams, track names as
    // handler names, the bibliographic German code, and a QuickTime chapter text track.
    public const string Mp4RemuxOfMkvH264Aac = """
    {
      "streams": [
        {
          "index": 0, "codec_name": "h264", "profile": "High", "codec_type": "video",
          "codec_tag_string": "avc1", "width": 1920, "height": 1080, "pix_fmt": "yuv420p",
          "level": 40, "avg_frame_rate": "34046000/1419877", "color_primaries": "bt709", "color_transfer": "bt709", "color_space": "bt709",
          "nb_frames": "34046",
          "disposition": { "default": 1, "forced": 0, "attached_pic": 0 },
          "tags": { "language": "und", "handler_name": "VideoHandler" }
        },
        {
          "index": 1, "codec_name": "aac", "profile": "LC", "codec_type": "audio", "codec_tag_string": "mp4a",
          "sample_rate": "48000", "channels": 2, "channel_layout": "stereo", "nb_frames": "66570",
          "disposition": { "default": 1, "forced": 0 },
          "tags": { "language": "jpn", "handler_name": "Japanese" }
        },
        {
          "index": 2, "codec_name": "aac", "profile": "LC", "codec_type": "audio", "codec_tag_string": "mp4a",
          "sample_rate": "48000", "channels": 2, "channel_layout": "stereo",
          "disposition": { "default": 0, "forced": 0, "dub": 1 },
          "tags": { "language": "deu", "handler_name": "Deutsch" }
        },
        {
          "index": 3, "codec_name": "bin_data", "codec_type": "data", "codec_tag_string": "text",
          "disposition": { "default": 0 },
          "tags": { "language": "eng", "handler_name": "SubtitleHandler" }
        }
      ],
      "chapters": [
        { "id": 0, "start_time": "0.000000", "end_time": "90.000000", "tags": { "title": "Opening" } },
        { "id": 1, "start_time": "90.000000", "end_time": "1330.000000", "tags": { "title": "Episode" } },
        { "id": 2, "start_time": "1330.000000", "end_time": "1420.500000", "tags": { "title": "Ending" } }
      ],
      "format": {
        "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "1420.522000",
        "tags": { "title": "Frieren - S01E02", "encoder": "Lavf61.7.100" }
      }
    }
    """;

    // A fansub release: styled ASS subtitles with the fonts they reference as attachments.
    public const string MkvAnimeAssWithFonts = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "profile": "High", "codec_type": "video", "width": 1920, "height": 1080, "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 }, "tags": { "language": "jpn" } },
        { "index": 2, "codec_name": "ass", "codec_type": "subtitle", "disposition": { "default": 1 }, "tags": { "language": "eng", "title": "Full Subtitles" } },
        { "index": 3, "codec_name": "ass", "codec_type": "subtitle", "disposition": { "default": 0, "forced": 1 }, "tags": { "language": "eng", "title": "Signs & Songs" } },
        { "index": 4, "codec_name": "ttf", "codec_type": "attachment", "tags": { "filename": "Roboto-Medium.ttf", "mimetype": "application/x-truetype-font" } },
        { "index": 5, "codec_name": "otf", "codec_type": "attachment", "tags": { "filename": "Gandhi Sans.otf", "mimetype": "font/otf" } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    // HEVC 10-bit HDR10 with AAC: MP4 tagged hvc1 lets Safari play it directly.
    public const string MkvHevcHdr10Aac = """
    {
      "streams": [
        {
          "index": 0, "codec_name": "hevc", "profile": "Main 10", "codec_type": "video", "codec_tag_string": "[0][0][0][0]",
          "width": 3840, "height": 2160, "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084", "color_primaries": "bt2020", "color_space": "bt2020nc",
          "side_data_list": [ { "side_data_type": "Mastering display metadata" }, { "side_data_type": "Content light level metadata" } ],
          "disposition": { "default": 1 }
        },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 6, "channel_layout": "5.1", "disposition": { "default": 1 }, "tags": { "language": "jpn" } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    public const string MkvHevcDolbyVision = """
    {
      "streams": [
        {
          "index": 0, "codec_name": "hevc", "profile": "Main 10", "codec_type": "video", "codec_tag_string": "[0][0][0][0]",
          "width": 3840, "height": 2160, "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084",
          "side_data_list": [ { "side_data_type": "DOVI configuration record", "dv_profile": 8 } ],
          "disposition": { "default": 1 }
        },
        { "index": 1, "codec_name": "eac3", "codec_type": "audio", "channels": 6, "disposition": { "default": 1 } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    // A BD remux whose default track is DTS: no browser plays it, remuxed or not.
    public const string MkvH264Dts = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "dts", "profile": "DTS-HD MA", "codec_type": "audio", "channels": 6, "disposition": { "default": 1 }, "tags": { "language": "jpn" } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    // Default AAC plays directly after a remux, but the TrueHD commentary cannot be copied into MP4.
    public const string MkvH264AacWithTrueHd = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 }, "tags": { "language": "jpn" } },
        { "index": 2, "codec_name": "truehd", "codec_type": "audio", "channels": 8, "disposition": { "default": 0 }, "tags": { "language": "eng" } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    public const string MkvH264AacSrt = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 } },
        { "index": 2, "codec_name": "subrip", "codec_type": "subtitle", "disposition": { "default": 0 }, "tags": { "language": "ger" } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    // Hi10P anime: 10-bit H.264 plays in no browser, so a remux cannot help.
    public const string MkvH264Hi10pAac = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "profile": "High 10", "codec_type": "video", "pix_fmt": "yuv420p10le", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    public const string Mp4H264Aac = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "codec_tag_string": "avc1", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "codec_tag_string": "mp4a", "channels": 2, "disposition": { "default": 1 } }
      ],
      "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "1440.000000" }
    }
    """;

    public const string WebMVp9Opus = """
    {
      "streams": [
        { "index": 0, "codec_name": "vp9", "codec_type": "video", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "opus", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    public const string MkvH264AacCoverArt = """
    {
      "streams": [
        { "index": 0, "codec_name": "h264", "codec_type": "video", "pix_fmt": "yuv420p", "disposition": { "default": 1 } },
        { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "disposition": { "default": 1 } },
        { "index": 2, "codec_name": "mjpeg", "codec_type": "video", "pix_fmt": "yuvj420p", "disposition": { "attached_pic": 1 } }
      ],
      "format": { "format_name": "matroska,webm", "duration": "1440.000000" }
    }
    """;

    // Applies a change to a fixture, e.g. to model an output that lost something.
    public static string Mutate(string json, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    public static JsonObject Stream(JsonObject root, int position) =>
        root["streams"]!.AsArray()[position]!.AsObject();
}
