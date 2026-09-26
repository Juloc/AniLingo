namespace AniLingo.Web.Features.Acquisition.Backup;

/// <summary>
/// One JSON bundle of every canonical acquisition settings store (P1 item 8). prowlarr.json and
/// sabnzbd.json carry their API keys encrypted with this installation's ASP.NET Core Data
/// Protection keys (the same protector each store already uses at rest); they are included as-is
/// rather than in plain text, but only decrypt again on an installation that shares the same Data
/// Protection key ring. Restoring on a different installation keeps those two files' non-secret
/// fields but requires re-entering the API keys if decryption fails on next load.
/// </summary>
public sealed record AcquisitionBackupBundle(
    int Version,
    DateTimeOffset ExportedAtUtc,
    Dictionary<string, string> Files);

/// <summary>One file's before/after state for a restore preview or the applied restore.</summary>
public sealed record AcquisitionBackupFileChange(
    string FileName,
    bool ExistedBefore,
    bool ContainsEncryptedSecret,
    bool IsValidJson,
    bool WouldChange);

public sealed record AcquisitionBackupPreview(
    bool CanRestore,
    IReadOnlyList<AcquisitionBackupFileChange> Files,
    IReadOnlyList<string> Errors);

public sealed record AcquisitionBackupRestoreResult(
    bool Success,
    int FilesWritten,
    IReadOnlyList<string> Errors);
