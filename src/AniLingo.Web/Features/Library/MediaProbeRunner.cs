using AniLingo.Web.Infrastructure;

namespace AniLingo.Web.Features.Library;

public enum MediaProbeRunStatus
{
    Completed,
    // ffprobe ran and rejected the file.
    Failed,
    // ffprobe could not be started or timed out; the attempt is retried later.
    Unavailable
}

public sealed record MediaProbeRun(
    MediaProbeRunStatus Status,
    string Output = "",
    string? Error = null);

// The only place Jularr invokes ffprobe. Everything else reads MediaInventoryService.
public interface IMediaProbeRunner
{
    Task<MediaProbeRun> ProbeAsync(string fullPath, CancellationToken cancellationToken);
}

public sealed class FfprobeMediaProbeRunner(MediaProcessRunner processRunner) : IMediaProbeRunner
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    public async Task<MediaProbeRun> ProbeAsync(string fullPath, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            "ffprobe",
            [
                "-v", "error",
                "-show_format",
                "-show_streams",
                "-of", "json",
                fullPath
            ],
            ProbeTimeout,
            cancellationToken);

        if (result is null)
        {
            return new MediaProbeRun(
                MediaProbeRunStatus.Unavailable,
                Error: "ffprobe could not be started or timed out.");
        }

        return result.ExitCode == 0
            ? new MediaProbeRun(MediaProbeRunStatus.Completed, result.Output)
            : new MediaProbeRun(
                MediaProbeRunStatus.Failed,
                Error: string.IsNullOrWhiteSpace(result.ErrorSummary)
                    ? $"ffprobe exited with code {result.ExitCode}."
                    : result.ErrorSummary);
    }
}
