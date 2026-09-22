using System.Collections.Concurrent;
using AniLingo.Web.Infrastructure;
using System.Text.Json;

namespace AniLingo.Web.Features.Subtitles;

public sealed record EmbeddedSubtitleStream(
    int Index,
    string Codec,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsText);

public sealed record EmbeddedSubtitleContent(
    string SourceKey,
    string Format,
    string Content);

public sealed class EmbeddedSubtitleExtractor(
    MediaProcessRunner processRunner,
    ILogger<EmbeddedSubtitleExtractor> logger)
{
    public const string SourcePrefix = "embedded:";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);

    private static readonly HashSet<string> TextCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass",
        "ssa",
        "subrip",
        "srt",
        "webvtt",
        "mov_text",
        "text"
    };

    private readonly ConcurrentDictionary<string, ProbeCacheEntry> probeCache =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<EmbeddedSubtitleStream>> ProbeStreamsAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return [];
        }

        if (probeCache.TryGetValue(fullPath, out var cached) &&
            cached.SizeBytes == info.Length &&
            cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
        {
            return cached.Streams;
        }

        var probe = await processRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-select_streams", "s",
                "-show_entries", "stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced",
                "-of", "json",
                fullPath
            ],
            ProcessTimeout,
            cancellationToken);

        if (probe is null || probe.ExitCode != 0)
        {
            if (probe is not null)
            {
                logger.LogWarning(
                    "ffprobe failed for {MediaPath}: {Error}",
                    fullPath,
                    probe.ErrorSummary);
            }

            return [];
        }

        try
        {
            var streams = ParseStreams(probe.Output);
            probeCache[fullPath] = new ProbeCacheEntry(
                info.Length,
                info.LastWriteTimeUtc,
                streams);
            return streams;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "ffprobe returned invalid subtitle JSON for {MediaPath}.",
                fullPath);
            return [];
        }
    }

    public async Task<EmbeddedSubtitleContent?> ExtractPreferredJapaneseAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var streams = await ProbeStreamsAsync(mediaPath, cancellationToken);
        var stream = SelectPreferredJapaneseTextStream(streams);

        return stream is null
            ? null
            : await ExtractTextStreamAsync(
                Path.GetFullPath(mediaPath),
                stream,
                cancellationToken);
    }

    public async Task<EmbeddedSubtitleContent?> ExtractTextStreamAsync(
        string mediaPath,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var streams = await ProbeStreamsAsync(fullPath, cancellationToken);
        var stream = streams.SingleOrDefault(x => x.Index == streamIndex && x.IsText);

        return stream is null
            ? null
            : await ExtractTextStreamAsync(fullPath, stream, cancellationToken);
    }

    private async Task<EmbeddedSubtitleContent?> ExtractTextStreamAsync(
        string fullPath,
        EmbeddedSubtitleStream stream,
        CancellationToken cancellationToken)
    {
        var extraction = await processRunner.RunAsync(
            "ffmpeg",
            [
                "-v", "error",
                "-nostdin",
                "-i", fullPath,
                "-map", $"0:{stream.Index}",
                "-c:s", "srt",
                "-f", "srt",
                "pipe:1"
            ],
            ProcessTimeout,
            cancellationToken);

        if (extraction is null || extraction.ExitCode != 0)
        {
            if (extraction is not null)
            {
                logger.LogWarning(
                    "ffmpeg could not extract subtitle stream {StreamIndex} from {MediaPath}: {Error}",
                    stream.Index,
                    fullPath,
                    extraction.ErrorSummary);
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(extraction.Output))
        {
            return null;
        }

        return new EmbeddedSubtitleContent(
            BuildSourceKey(fullPath, stream.Index),
            "srt",
            extraction.Output);
    }

    public static IReadOnlyList<EmbeddedSubtitleStream> ParseStreams(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);

        if (!document.RootElement.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<EmbeddedSubtitleStream>();

        foreach (var item in streams.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var indexElement) ||
                !indexElement.TryGetInt32(out var index))
            {
                continue;
            }

            var codec = ReadString(item, "codec_name");
            if (string.IsNullOrWhiteSpace(codec))
            {
                continue;
            }

            string? language = null;
            string? title = null;

            if (item.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                language = ReadString(tags, "language");
                title = ReadString(tags, "title");
            }

            var isDefault = false;
            var isForced = false;

            if (item.TryGetProperty("disposition", out var disposition) &&
                disposition.ValueKind == JsonValueKind.Object)
            {
                isDefault = ReadFlag(disposition, "default");
                isForced = ReadFlag(disposition, "forced");
            }

            result.Add(new EmbeddedSubtitleStream(
                index,
                codec,
                language,
                title,
                isDefault,
                isForced,
                TextCodecs.Contains(codec)));
        }

        return result;
    }

    public static EmbeddedSubtitleStream? SelectPreferredJapaneseTextStream(string probeJson) =>
        SelectPreferredJapaneseTextStream(ParseStreams(probeJson));

    private static EmbeddedSubtitleStream? SelectPreferredJapaneseTextStream(
        IEnumerable<EmbeddedSubtitleStream> streams) =>
        streams
            .Where(x => x.IsText && IsJapanese(x.Language, x.Title))
            .OrderBy(x => x.IsForced)
            .ThenBy(x => LooksLikeSignsOrSongs(x.Title))
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Index)
            .FirstOrDefault();

    public static string BuildSourceKey(string mediaPath, int streamIndex) =>
        $"{BuildSourcePrefix(mediaPath)}{streamIndex}";

    public static string BuildSourcePrefix(string mediaPath) =>
        $"{SourcePrefix}{Path.GetFullPath(mediaPath)}#stream=";

    private static bool IsJapanese(string? language, string? title)
    {
        var normalizedLanguage = language?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedLanguage) &&
            !normalizedLanguage.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedLanguage.Equals("ja", StringComparison.OrdinalIgnoreCase)
                || normalizedLanguage.Equals("jpn", StringComparison.OrdinalIgnoreCase)
                || normalizedLanguage.Equals("japanese", StringComparison.OrdinalIgnoreCase);
        }

        return title?.Contains("japanese", StringComparison.OrdinalIgnoreCase) == true
            || title?.Contains("日本語", StringComparison.Ordinal) == true;
    }

    private static bool LooksLikeSignsOrSongs(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return title.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || title.Contains("song", StringComparison.OrdinalIgnoreCase)
            || title.Contains("karaoke", StringComparison.OrdinalIgnoreCase)
            || title.Contains("forced", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadFlag(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var flag) &&
        flag != 0;

    private sealed record ProbeCacheEntry(
        long SizeBytes,
        DateTime LastWriteTimeUtc,
        IReadOnlyList<EmbeddedSubtitleStream> Streams);
}
