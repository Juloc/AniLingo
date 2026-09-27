using System.Globalization;

namespace Jularr.Web.Features.Playback.Decision;

/// <summary>
/// Builds the ffmpeg arguments that execute a <see cref="PlaybackPlan"/>. Every value comes
/// from the server-side plan (stream indexes, codec names from a fixed set, integers); the
/// source path is resolved by the server and passed as one argument, never through a shell.
/// </summary>
public static class PlaybackDeliveryCommand
{
    public const int HlsSegmentSeconds = 4;

    // Linear-light tone mapping to BT.709 SDR; the H.264 output is always 8-bit SDR.
    public const string ToneMapFilter =
        "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv";

    private static readonly HashSet<string> SupportedEncoders = new(StringComparer.Ordinal)
    {
        PlaybackServerCapabilities.SoftwareH264Encoder
    };

    public static IReadOnlyList<string> Progressive(
        string sourcePath,
        PlaybackPlan plan,
        double startSeconds)
    {
        var arguments = Input(sourcePath, plan, startSeconds);
        arguments.AddRange([
            "-max_muxing_queue_size", "2048",
            "-avoid_negative_ts", "make_zero",
            "-movflags", "+frag_keyframe+empty_moov+default_base_moof",
            "-frag_duration", "1000000",
            "-f", "mp4",
            "pipe:1"
        ]);
        return arguments;
    }

    /// <summary>
    /// An EVENT playlist: segments are only appended, so a player that starts at the
    /// beginning never loses its place when ffmpeg (a fast remux) runs ahead of playback.
    /// Segments far behind the player are pruned by the server
    /// (<see cref="HlsPlaybackSessionManager.PruneBehind"/>) to bound disk use.
    /// </summary>
    public static IReadOnlyList<string> Hls(
        string sourcePath,
        PlaybackPlan plan,
        double startSeconds,
        string directory)
    {
        var arguments = Input(sourcePath, plan, startSeconds, overwrite: true);
        arguments.AddRange([
            "-max_muxing_queue_size", "2048",
            "-avoid_negative_ts", "make_zero",
            "-f", "hls",
            "-hls_time", HlsSegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_list_size", "0",
            "-hls_playlist_type", "event",
            "-hls_segment_type", "fmp4",
            "-hls_fmp4_init_filename", "init.mp4",
            "-hls_flags", "independent_segments",
            "-hls_segment_filename", Path.Combine(directory, "segment-%05d.m4s"),
            Path.Combine(directory, "index.m3u8")
        ]);
        return arguments;
    }

    /// <summary>
    /// Adds <c>EXT-X-START:TIME-OFFSET=0</c> so native players begin at the start of the
    /// (still growing) playlist instead of at its live edge.
    /// </summary>
    public static string StartAtBeginning(string playlist)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        if (playlist.Contains("#EXT-X-START", StringComparison.Ordinal))
        {
            return playlist;
        }

        const string header = "#EXTM3U";
        return playlist.StartsWith(header, StringComparison.Ordinal)
            ? header + "\n#EXT-X-START:TIME-OFFSET=0,PRECISE=YES" + playlist[header.Length..]
            : playlist;
    }

    private static List<string> Input(
        string sourcePath,
        PlaybackPlan plan,
        double startSeconds,
        bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Mode is not (PlaybackDeliveryMode.DirectStream or PlaybackDeliveryMode.Transcode) ||
            plan.Video is not { } video)
        {
            throw new ArgumentException("Only Direct Stream and Transcode plans run ffmpeg.", nameof(plan));
        }

        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        }

        var arguments = new List<string> { "-v", "error", "-nostdin" };
        if (overwrite)
        {
            arguments.Add("-y");
        }

        arguments.AddRange(["-fflags", "+genpts"]);
        if (startSeconds > 0)
        {
            arguments.AddRange(["-ss", startSeconds.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        arguments.AddRange(["-i", Path.GetFullPath(sourcePath)]);

        if (video.Copy)
        {
            arguments.AddRange(["-map", "0:v:0", "-c:v", "copy"]);
            if (video.TagHevcAsHvc1)
            {
                arguments.AddRange(["-tag:v", "hvc1"]);
            }
        }
        else
        {
            AddVideoEncode(arguments, plan, video);
        }

        if (plan.Audio is { } audio)
        {
            arguments.AddRange(["-map", LivePlaybackCommand.AudioMap(audio.StreamIndex)]);
            if (audio.Copy)
            {
                arguments.AddRange(["-c:a", "copy"]);
            }
            else
            {
                arguments.AddRange([
                    "-c:a", "aac",
                    "-b:a", $"{(audio.BitrateKbps ?? 192).ToString(CultureInfo.InvariantCulture)}k",
                    "-ac", (audio.OutputChannels ?? 2).ToString(CultureInfo.InvariantCulture)
                ]);
            }
        }

        arguments.AddRange(["-sn", "-dn"]);
        return arguments;
    }

    private static void AddVideoEncode(List<string> arguments, PlaybackPlan plan, PlaybackVideoOutput video)
    {
        var encoder = video.Encoder is { } requested && SupportedEncoders.Contains(requested)
            ? requested
            : PlaybackServerCapabilities.SoftwareH264Encoder;

        var filters = new List<string>();
        if (video.ToneMap)
        {
            filters.Add(ToneMapFilter);
        }

        if (video.MaxOutputHeight is { } maxHeight && maxHeight > 0)
        {
            filters.Add($"scale=-2:min(ih\\,{maxHeight.ToString(CultureInfo.InvariantCulture)})");
        }

        filters.Add("format=yuv420p");

        if (video.BurnInSubtitleStreamIndex is { } subtitleIndex)
        {
            var index = subtitleIndex.ToString(CultureInfo.InvariantCulture);
            arguments.AddRange([
                "-filter_complex",
                $"[0:v:0][0:{index}]overlay=eof_action=pass,{string.Join(',', filters)}[vout]",
                "-map", "[vout]"
            ]);
        }
        else
        {
            arguments.AddRange(["-map", "0:v:0", "-vf", string.Join(',', filters)]);
        }

        var bitrate = Math.Max(300, video.TargetBitrateKbps ?? PlaybackDecisionEngine.DefaultVideoKbps(video.MaxOutputHeight ?? 1080));
        arguments.AddRange([
            "-c:v", encoder,
            "-preset", "veryfast",
            "-profile:v", "high",
            "-b:v", $"{bitrate.ToString(CultureInfo.InvariantCulture)}k",
            "-maxrate", $"{(bitrate * 3 / 2).ToString(CultureInfo.InvariantCulture)}k",
            "-bufsize", $"{(bitrate * 2).ToString(CultureInfo.InvariantCulture)}k"
        ]);

        if (plan.Transport == PlaybackTransport.Hls)
        {
            arguments.AddRange(["-force_key_frames", $"expr:gte(t,n_forced*{HlsSegmentSeconds})"]);
        }
    }
}
