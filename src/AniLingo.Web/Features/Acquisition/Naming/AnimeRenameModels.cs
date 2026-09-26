using System.Security.Cryptography;
using System.Text;

namespace AniLingo.Web.Features.Acquisition.Naming;

public enum AnimeRenameItemStatus
{
    Unchanged,
    Rename,
    Blocked
}

public sealed record AnimeRenameMove(string SourcePath, string TargetPath);

// SourcePath is the current location; TargetPath the final location after every move of the
// plan (including a series-folder move). Sidecars are subtitle/NFO files named after the media file.
public sealed record AnimeRenamePlanItem(
    Guid MediaFileId,
    Guid EpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    string SourcePath,
    string TargetPath,
    AnimeRenameItemStatus Status,
    string? Reason,
    IReadOnlyList<AnimeRenameMove> Sidecars,
    string SourceSeriesFolder);

public sealed record AnimeRenamePlan(
    Guid AnimeId,
    string AnimeTitle,
    string AnimeKey,
    string TargetAnimeKey,
    AnimeNamingResolution Naming,
    bool RenameSeriesFolder,
    IReadOnlyList<AnimeRenameMove> FolderMoves,
    IReadOnlyList<AnimeRenamePlanItem> Items,
    IReadOnlyList<string> BlockingReasons)
{
    public int RenameCount => Items.Count(item => item.Status == AnimeRenameItemStatus.Rename);

    public int BlockedCount => Items.Count(item => item.Status == AnimeRenameItemStatus.Blocked);

    // A series-folder move relocates every file, so it runs only when no file is blocked.
    // A files-only rename executes the renamable files and leaves blocked ones untouched.
    public bool CanExecute =>
        BlockingReasons.Count == 0 &&
        (RenameCount > 0 || FolderMoves.Count > 0) &&
        (!RenameSeriesFolder || BlockedCount == 0);

    // Identifies exactly the moves a preview showed, so execution refuses a changed plan.
    public string Fingerprint
    {
        get
        {
            var builder = new StringBuilder();
            builder.Append(AnimeId.ToString("N")).Append('|').Append(Naming.Profile.Id).Append('|').Append(TargetAnimeKey);
            foreach (var move in FolderMoves)
            {
                builder.Append("|D:").Append(move.SourcePath).Append('>').Append(move.TargetPath);
            }

            foreach (var item in Items.Where(item => item.Status == AnimeRenameItemStatus.Rename).OrderBy(item => item.SourcePath, StringComparer.Ordinal))
            {
                builder.Append("|F:").Append(item.SourcePath).Append('>').Append(item.TargetPath);
                foreach (var sidecar in item.Sidecars)
                {
                    builder.Append("|S:").Append(sidecar.SourcePath).Append('>').Append(sidecar.TargetPath);
                }
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..32];
        }
    }
}

public sealed record AnimeRenameResult(
    bool Success,
    string Message,
    Guid? OperationId,
    IReadOnlyList<AnimeRenameMove> Moves);
