using System.Buffers.Binary;
using AniLingo.Web.Infrastructure;

namespace AniLingo.Web.Features.MediaSegments;

// Decodes one time window of a media file's audio to mono 16-bit PCM at
// AudioFingerprint.SampleRateHz. Kept as an interface so fingerprint matching can be
// unit-tested with synthetic PCM without ffmpeg or a real media file; only
// FfmpegAudioWindowDecoder touches a process or the filesystem.
public interface IAudioWindowDecoder
{
    Task<short[]?> DecodeAsync(
        string mediaPath,
        double startSeconds,
        double lengthSeconds,
        CancellationToken cancellationToken);
}

// Shells out to ffmpeg to decode a bounded window of audio; never reads or writes the source
// file itself beyond that. Returns null on any failure (missing ffmpeg, timeout, bad input) so
// callers degrade to "no fingerprint" instead of throwing.
public sealed class FfmpegAudioWindowDecoder(
    MediaProcessRunner processRunner,
    ILogger<FfmpegAudioWindowDecoder> logger,
    string ffmpegExecutable = "ffmpeg") : IAudioWindowDecoder
{
    public static readonly TimeSpan DecodeTimeout = TimeSpan.FromMinutes(2);

    public async Task<short[]?> DecodeAsync(
        string mediaPath,
        double startSeconds,
        double lengthSeconds,
        CancellationToken cancellationToken)
    {
        if (lengthSeconds <= 0 || !double.IsFinite(lengthSeconds) || !File.Exists(mediaPath))
        {
            return null;
        }

        var temporaryFile = Path.Combine(Path.GetTempPath(), $"anilingo-fp-{Guid.NewGuid():N}.pcm");
        try
        {
            var result = await processRunner.RunAsync(
                ffmpegExecutable,
                BuildArguments(mediaPath, startSeconds, lengthSeconds, temporaryFile),
                DecodeTimeout,
                cancellationToken);

            if (result is null)
            {
                return null;
            }

            if (result.ExitCode != 0 || !File.Exists(temporaryFile))
            {
                logger.LogDebug(
                    "ffmpeg audio window decode failed for {Path}: {Error}",
                    mediaPath,
                    result.ErrorSummary);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(temporaryFile, cancellationToken);
            if (bytes.Length < 2)
            {
                return null;
            }

            var samples = new short[bytes.Length / 2];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));
            }

            return samples;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(temporaryFile);
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string mediaPath,
        double startSeconds,
        double lengthSeconds,
        string outputPath) =>
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-ss", startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-t", lengthSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-i", mediaPath,
            "-vn", "-sn", "-dn",
            "-ac", "1",
            "-ar", AudioFingerprint.SampleRateHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "s16le",
            outputPath
        ];

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
        catch (UnauthorizedAccessException)
        {
        }
    }
}
