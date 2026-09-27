namespace Jularr.Web.Features.Media.Optimization;

public sealed record MediaFileReplacement(
    Guid MediaFileId,
    string SourcePath,
    string TargetPath);

public enum MediaReplacementVerdict
{
    Allowed,
    // Other library work touches the same files right now; ask again shortly.
    Wait,
    Blocked
}

// An Allowed check may carry a lease that keeps the participant's own writers (e.g. imports) out
// until the optimizer has renamed the file and recorded the new path; the optimizer disposes it.
public sealed record MediaReplacementCheck(
    MediaReplacementVerdict Verdict,
    string? Reason = null,
    IDisposable? Lease = null)
{
    public static MediaReplacementCheck Allowed { get; } = new(MediaReplacementVerdict.Allowed);
}

// A feature that keys its own records by library media path (acquisition ownership, import
// history). The optimizer asks every participant before it replaces a file and tells them after,
// so the media feature does not need to know who else tracks paths.
public interface IMediaFileReplacementParticipant
{
    // Must not block: return Wait while the participant's own work touches the files.
    Task<MediaReplacementCheck> CheckAsync(
        MediaFileReplacement replacement,
        CancellationToken cancellationToken);

    // Called after the database names the target; must be idempotent (recovery may repeat it).
    Task OnReplacedAsync(
        MediaFileReplacement replacement,
        CancellationToken cancellationToken);
}
