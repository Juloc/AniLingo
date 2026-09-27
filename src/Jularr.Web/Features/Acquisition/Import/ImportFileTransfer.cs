namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// The one implementation of the owner's import mode (Move / Copy / Hardlink / Hardlink or copy)
/// for a single file, shared by every media importer. It never overwrites an existing file;
/// conflict policy (skip, rename, manual review) stays with the caller.
/// </summary>
public sealed class ImportFileTransfer(IHardLinkCreator hardLinks)
{
    /// <summary>The file action and cross-filesystem fallback an import mode stands for.</summary>
    public static (AnimeImportFileAction Action, bool AllowHardlinkFallbackToCopy) Resolve(ImportMode mode) =>
        mode switch
        {
            ImportMode.Copy => (AnimeImportFileAction.Copy, false),
            ImportMode.Hardlink => (AnimeImportFileAction.Hardlink, false),
            ImportMode.HardlinkOrCopy => (AnimeImportFileAction.Hardlink, true),
            _ => (AnimeImportFileAction.Move, false)
        };

    public void Transfer(string sourcePath, string destinationPath, ImportMode mode)
    {
        var (action, allowFallback) = Resolve(mode);
        Transfer(sourcePath, destinationPath, action, allowFallback);
    }

    /// <exception cref="CrossDeviceLinkException">A hardlink crosses filesystems and no copy fallback is allowed.</exception>
    public void Transfer(
        string sourcePath,
        string destinationPath,
        AnimeImportFileAction action,
        bool allowHardlinkFallbackToCopy)
    {
        switch (action)
        {
            case AnimeImportFileAction.Copy:
                File.Copy(sourcePath, destinationPath, overwrite: false);
                break;

            case AnimeImportFileAction.Hardlink:
                try
                {
                    hardLinks.CreateHardLink(sourcePath, destinationPath);
                }
                catch (CrossDeviceLinkException) when (allowHardlinkFallbackToCopy)
                {
                    File.Copy(sourcePath, destinationPath, overwrite: false);
                }

                break;

            default:
                File.Move(sourcePath, destinationPath, overwrite: false);
                break;
        }
    }
}
