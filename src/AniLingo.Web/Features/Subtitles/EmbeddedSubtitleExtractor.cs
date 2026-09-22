using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AniLingo.Web.Infrastructure;

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

public enum AudioTranscriptionStatus
{
    None,
    Queued,
    Processing,
    Ready,
    Failed
}

public sealed record AudioTranscriptionState(
    AudioTranscriptionStatus Status,
    string? Message = null);

public sealed class EmbeddedSubtitleExtractor(
    MediaProcessRunner processRunner,
    ILogger<EmbeddedSubtitleExtractor> logger)
{
    public const string SourcePrefix = "embedded:";
    public const string TranscriptionSourcePrefix = "transcribed:";

    private const string TranscriptionRoot = "/data/transcription-cache";
    private const string WhisperModelPath = "/data/whisper/ggml-small-q5_1.bin";
    private const string WhisperModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin";
    private const string WhisperModelSha256 =
        "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb";

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan LongProcessTimeout = TimeSpan.FromHours(2);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

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
    private readonly ConcurrentDictionary<string, AudioTranscriptionState> transcriptionStates =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim transcriptionGate = new(1, 1);

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

    public AudioTranscriptionState GetAudioTranscriptionState(string mediaPath)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        return transcriptionStates.TryGetValue(fullPath, out var state)
            ? state
            : new AudioTranscriptionState(AudioTranscriptionStatus.None);
    }

    public bool TryQueueAudioTranscription(string mediaPath)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var queued = new AudioTranscriptionState(AudioTranscriptionStatus.Queued);

        while (true)
        {
            var current = GetAudioTranscriptionState(fullPath);
            if (current.Status is AudioTranscriptionStatus.Queued
                or AudioTranscriptionStatus.Processing
                or AudioTranscriptionStatus.Ready)
            {
                return false;
            }

            if (current.Status == AudioTranscriptionStatus.None)
            {
                if (transcriptionStates.TryAdd(fullPath, queued))
                {
                    return true;
                }

                continue;
            }

            if (transcriptionStates.TryUpdate(fullPath, queued, current))
            {
                return true;
            }
        }
    }

    public void MarkAudioTranscriptionProcessing(string mediaPath) =>
        transcriptionStates[Path.GetFullPath(mediaPath)] =
            new AudioTranscriptionState(AudioTranscriptionStatus.Processing);

    public void MarkAudioTranscriptionReady(string mediaPath) =>
        transcriptionStates[Path.GetFullPath(mediaPath)] =
            new AudioTranscriptionState(AudioTranscriptionStatus.Ready);

    public void MarkAudioTranscriptionFailed(string mediaPath, string message) =>
        transcriptionStates[Path.GetFullPath(mediaPath)] =
            new AudioTranscriptionState(AudioTranscriptionStatus.Failed, message);

    public async Task<EmbeddedSubtitleContent?> TranscribeJapaneseAudioAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            MarkAudioTranscriptionFailed(fullPath, "Media file no longer exists.");
            return null;
        }

        await transcriptionGate.WaitAsync(cancellationToken);
        try
        {
            MarkAudioTranscriptionProcessing(fullPath);

            Directory.CreateDirectory(TranscriptionRoot);
            var cacheKey = BuildTranscriptionCacheKey(
                fullPath,
                info.Length,
                info.LastWriteTimeUtc);
            var outputPrefix = Path.Combine(TranscriptionRoot, cacheKey);
            var srtPath = outputPrefix + ".srt";

            if (File.Exists(srtPath) && new FileInfo(srtPath).Length > 0)
            {
                var cached = await File.ReadAllTextAsync(srtPath, cancellationToken);
                MarkAudioTranscriptionReady(fullPath);
                return new EmbeddedSubtitleContent(
                    BuildTranscriptionSourceKey(fullPath),
                    "srt",
                    cached);
            }

            if (!await EnsureWhisperModelAsync(cancellationToken))
            {
                MarkAudioTranscriptionFailed(
                    fullPath,
                    "Could not download or verify the local Whisper model.");
                return null;
            }

            var audioStreamIndex = await ProbePreferredJapaneseAudioStreamIndexAsync(
                fullPath,
                cancellationToken);
            if (audioStreamIndex is null)
            {
                MarkAudioTranscriptionFailed(fullPath, "No audio stream was found.");
                return null;
            }

            var wavPath = outputPrefix + ".input.wav";
            TryDelete(wavPath);
            TryDelete(srtPath);

            try
            {
                var extraction = await processRunner.RunAsync(
                    "ffmpeg",
                    [
                        "-v", "error",
                        "-nostdin",
                        "-y",
                        "-i", fullPath,
                        "-map", $"0:{audioStreamIndex.Value}",
                        "-vn",
                        "-ac", "1",
                        "-ar", "16000",
                        "-c:a", "pcm_s16le",
                        wavPath
                    ],
                    LongProcessTimeout,
                    cancellationToken);

                if (extraction is null ||
                    extraction.ExitCode != 0 ||
                    !File.Exists(wavPath) ||
                    new FileInfo(wavPath).Length == 0)
                {
                    MarkAudioTranscriptionFailed(
                        fullPath,
                        "Could not extract audio for transcription.");
                    return null;
                }

                var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
                var transcription = await processRunner.RunAsync(
                    "whisper-cli",
                    [
                        "-m", WhisperModelPath,
                        "-f", wavPath,
                        "-l", "ja",
                        "-t", threads.ToString(),
                        "-osrt",
                        "-of", outputPrefix,
                        "-np"
                    ],
                    LongProcessTimeout,
                    cancellationToken);

                if (transcription is null ||
                    transcription.ExitCode != 0 ||
                    !File.Exists(srtPath) ||
                    new FileInfo(srtPath).Length == 0)
                {
                    if (transcription is not null)
                    {
                        logger.LogWarning(
                            "whisper.cpp failed for {MediaPath}: {Error}",
                            fullPath,
                            transcription.ErrorSummary);
                    }

                    TryDelete(srtPath);
                    MarkAudioTranscriptionFailed(
                        fullPath,
                        "Japanese audio transcription failed.");
                    return null;
                }

                var content = await File.ReadAllTextAsync(srtPath, cancellationToken);
                if (string.IsNullOrWhiteSpace(content))
                {
                    TryDelete(srtPath);
                    MarkAudioTranscriptionFailed(
                        fullPath,
                        "Japanese audio transcription returned no text.");
                    return null;
                }

                MarkAudioTranscriptionReady(fullPath);
                return new EmbeddedSubtitleContent(
                    BuildTranscriptionSourceKey(fullPath),
                    "srt",
                    content);
            }
            finally
            {
                TryDelete(wavPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not transcribe Japanese audio for {MediaPath}.",
                fullPath);
            MarkAudioTranscriptionFailed(
                fullPath,
                "Could not write the transcription cache.");
            return null;
        }
        finally
        {
            transcriptionGate.Release();
        }
    }

    public static string BuildTranscriptionSourceKey(string mediaPath) =>
        $"{TranscriptionSourcePrefix}{Path.GetFullPath(mediaPath)}#audio=ja";

    public static string BuildTranscriptionCacheKey(
        string mediaPath,
        long sizeBytes,
        DateTime sourceUpdatedAt)
    {
        var fingerprint =
            $"{Path.GetFullPath(mediaPath)}\n{sizeBytes}\n{sourceUpdatedAt.ToUniversalTime().Ticks}";
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();
    }

    public static int? SelectPreferredJapaneseAudioStreamIndex(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);
        if (!document.RootElement.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int? first = null;
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("index", out var indexElement) ||
                !indexElement.TryGetInt32(out var index))
            {
                continue;
            }

            first ??= index;

            string? language = null;
            string? title = null;
            if (stream.TryGetProperty("tags", out var tags) &&
                tags.ValueKind == JsonValueKind.Object)
            {
                language = ReadString(tags, "language");
                title = ReadString(tags, "title");
            }

            if (IsJapanese(language, title))
            {
                return index;
            }
        }

        return first;
    }

    public static bool IsJapanese(string? language, string? title)
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

    private async Task<int?> ProbePreferredJapaneseAudioStreamIndexAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var probe = await processRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-select_streams", "a",
                "-show_entries", "stream=index:stream_tags=language,title",
                "-of", "json",
                fullPath
            ],
            ProcessTimeout,
            cancellationToken);

        if (probe is null || probe.ExitCode != 0)
        {
            return null;
        }

        try
        {
            return SelectPreferredJapaneseAudioStreamIndex(probe.Output);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "ffprobe returned invalid audio JSON for {MediaPath}.",
                fullPath);
            return null;
        }
    }

    private async Task<bool> EnsureWhisperModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(WhisperModelPath) &&
            await HasExpectedWhisperModelHashAsync(WhisperModelPath, cancellationToken))
        {
            return true;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(WhisperModelPath)!);
        var temporaryPath = WhisperModelPath + ".download";
        TryDelete(temporaryPath);

        var download = await processRunner.RunAsync(
            "curl",
            [
                "-fL",
                "--retry", "5",
                "--retry-delay", "2",
                "--retry-all-errors",
                "-o", temporaryPath,
                WhisperModelUrl
            ],
            DownloadTimeout,
            cancellationToken);

        if (download is null ||
            download.ExitCode != 0 ||
            !File.Exists(temporaryPath) ||
            !await HasExpectedWhisperModelHashAsync(temporaryPath, cancellationToken))
        {
            TryDelete(temporaryPath);
            return false;
        }

        File.Move(temporaryPath, WhisperModelPath, overwrite: true);
        return true;
    }

    private static async Task<bool> HasExpectedWhisperModelHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(hash),
            WhisperModelSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
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
