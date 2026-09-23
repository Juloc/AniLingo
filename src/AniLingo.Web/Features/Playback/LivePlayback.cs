using System.Diagnostics;
using System.Globalization;

namespace AniLingo.Web.Features.Playback;

public static class LivePlaybackCommand
{
    public static IReadOnlyList<string> BuildArguments(
        string sourcePath,
        PlaybackPreparationPlan plan,
        double startSeconds = 0)
    {
        if (!plan.CanPrepare || plan.Kind is null)
        {
            throw new ArgumentException("Playback plan cannot be streamed.", nameof(plan));
        }

        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds));
        }

        var arguments = new List<string>
        {
            "-v", "error",
            "-nostdin",
            "-fflags", "+genpts"
        };

        if (startSeconds > 0)
        {
            arguments.AddRange([
                "-ss",
                startSeconds.ToString("0.###", CultureInfo.InvariantCulture)
            ]);
        }

        arguments.AddRange([
            "-i", Path.GetFullPath(sourcePath),
            "-map", "0:v:0",
            "-sn",
            "-dn"
        ]);

        if (plan.VideoMode == PlaybackVideoMode.Copy)
        {
            arguments.AddRange(["-c:v", "copy"]);
            if (plan.TagHevcAsHvc1)
            {
                arguments.AddRange(["-tag:v", "hvc1"]);
            }
        }
        else
        {
            arguments.AddRange([
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "22",
                "-pix_fmt", "yuv420p"
            ]);
        }

        switch (plan.AudioMode)
        {
            case PlaybackAudioMode.Copy:
                arguments.AddRange(["-map", "0:a:0?", "-c:a", "copy"]);
                break;
            case PlaybackAudioMode.Aac:
                arguments.AddRange([
                    "-map", "0:a:0?",
                    "-c:a", "aac",
                    "-b:a", "192k"
                ]);
                break;
        }

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
}

public sealed class LivePlaybackStream : Stream
{
    private readonly Process process;
    private readonly Stream output;
    private readonly Task stderrDrain;
    private bool disposed;

    private LivePlaybackStream(Process process)
    {
        this.process = process;
        output = process.StandardOutput.BaseStream;
        stderrDrain = process.StandardError.ReadToEndAsync();
    }

    public static LivePlaybackStream Start(
        string sourcePath,
        PlaybackPreparationPlan plan,
        double startSeconds = 0)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in LivePlaybackCommand.BuildArguments(
                     sourcePath,
                     plan,
                     startSeconds))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Could not start ffmpeg playback stream.");
        }

        return new LivePlaybackStream(process);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        output.Read(buffer, offset, count);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        output.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        output.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposing || disposed)
        {
            base.Dispose(disposing);
            return;
        }

        disposed = true;

        try
        {
            output.Dispose();
        }
        finally
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

            process.Dispose();
            _ = stderrDrain;
        }

        base.Dispose(disposing);
    }
}
