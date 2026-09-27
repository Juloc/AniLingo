using System.Globalization;
using Jularr.Web.Infrastructure;

namespace Jularr.Web.Features.Media.Optimization;

public sealed record MediaRemuxRun(bool Succeeded, string? Error = null);

// Writes a stream-copied container for a MediaRemuxPlan. The only ffmpeg invocation of the
// post-download optimizer; tests replace it so they never need ffmpeg installed.
public interface IMediaContainerRemuxer
{
    Task<MediaRemuxRun> RemuxAsync(
        string sourcePath,
        string outputPath,
        MediaRemuxPlan plan,
        CancellationToken cancellationToken);
}

public sealed class FfmpegMediaContainerRemuxer(MediaProcessRunner processRunner) : IMediaContainerRemuxer
{
    // Stream copy is bound by disk throughput; this only guards against a hung process.
    private static readonly TimeSpan RemuxTimeout = TimeSpan.FromHours(3);

    public async Task<MediaRemuxRun> RemuxAsync(
        string sourcePath,
        string outputPath,
        MediaRemuxPlan plan,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "ffmpeg",
            BuildArguments(sourcePath, outputPath, plan),
            RemuxTimeout,
            cancellationToken);

        if (result is null)
        {
            return new(false, "ffmpeg could not be started or timed out.");
        }

        return result.ExitCode == 0
            ? new(true)
            : new(false, string.IsNullOrWhiteSpace(result.ErrorSummary)
                ? $"ffmpeg exited with code {result.ExitCode}."
                : result.ErrorSummary);
    }

    // Maps every stream in source order and copies it: no codec is ever re-encoded, and the
    // output stream indices equal the source indices, so stream-index references stay valid.
    public static IReadOnlyList<string> BuildArguments(
        string sourcePath,
        string outputPath,
        MediaRemuxPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var arguments = new List<string>
        {
            "-v", "error",
            "-nostdin",
            "-y",
            "-i", sourcePath,
            "-map", "0",
            "-map_metadata", "0",
            "-map_chapters", "0",
            "-c", "copy",
            "-max_muxing_queue_size", "4096"
        };

        if (plan.HevcStreamIndex is { } hevcIndex)
        {
            arguments.AddRange([$"-tag:{hevcIndex.ToString(CultureInfo.InvariantCulture)}", "hvc1"]);
        }

        foreach (var (index, title) in plan.StreamTitles.OrderBy(x => x.Key))
        {
            arguments.AddRange([
                $"-metadata:s:{index.ToString(CultureInfo.InvariantCulture)}",
                $"handler_name={title}"
            ]);
        }

        arguments.AddRange(["-movflags", "+faststart", "-f", "mp4", outputPath]);
        return arguments;
    }
}
