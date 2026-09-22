using System.Diagnostics;
using System.Text.Json;

namespace AniLingo.Web.Features.Subtitles;

public sealed record EmbeddedSubtitleStream(
    int Index,
    string Codec,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced);

public sealed record EmbeddedSubtitleContent(
    string SourceKey,
    string Format,
    string Content);

public sealed class EmbeddedSubtitleExtractor(ILogger<EmbeddedSubtitleExtractor> logger)
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

    public async Task<EmbeddedSubtitleContent?> ExtractPreferredJapaneseAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(mediaPath);
        var probe = await RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-select_streams", "s",
                "-show_entries", "stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced",
                "-of", "json",
                fullPath
            ],
            cancellationToken);

        if (probe is null || probe.ExitCode != 0)
        {
            if (probe is not null)
            {
                logger.LogWarning(
                    "ffprobe failed for {MediaPath}: {Error}",
                    fullPath,
                    TrimError(probe.Error));
            }

            return null;
        }

        var stream = SelectPreferredJapaneseTextStream(probe.Output);
        if (stream is null)
        {
            return null;
        }

        var extraction = await RunAsync(
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
            cancellationToken);

        if (extraction is null || extraction.ExitCode != 0)
        {
            if (extraction is not null)
            {
                logger.LogWarning(
                    "ffmpeg could not extract Japanese subtitle stream {StreamIndex} from {MediaPath}: {Error}",
                    stream.Index,
                    fullPath,
                    TrimError(extraction.Error));
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

    public static EmbeddedSubtitleStream? SelectPreferredJapaneseTextStream(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);

        if (!document.RootElement.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var candidates = new List<EmbeddedSubtitleStream>();

        foreach (var item in streams.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var indexElement) ||
                !indexElement.TryGetInt32(out var index))
            {
                continue;
            }

            var codec = ReadString(item, "codec_name");
            if (string.IsNullOrWhiteSpace(codec) || !TextCodecs.Contains(codec))
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

            if (!IsJapanese(language, title))
            {
                continue;
            }

            var isDefault = false;
            var isForced = false;

            if (item.TryGetProperty("disposition", out var disposition) &&
                disposition.ValueKind == JsonValueKind.Object)
            {
                isDefault = ReadFlag(disposition, "default");
                isForced = ReadFlag(disposition, "forced");
            }

            candidates.Add(new EmbeddedSubtitleStream(
                index,
                codec,
                language,
                title,
                isDefault,
                isForced));
        }

        return candidates
            .OrderBy(x => x.IsForced)
            .ThenBy(x => LooksLikeSignsOrSongs(x.Title))
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Index)
            .FirstOrDefault();
    }

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

    private async Task<ProcessResult?> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "Could not start {Executable}.", executable);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProcessTimeout);

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogWarning(
                "{Executable} exceeded the {TimeoutSeconds}s media inspection timeout.",
                executable,
                ProcessTimeout.TotalSeconds);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string TrimError(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
