namespace Jularr.Web.Features.Books;

/// <summary>
/// Where a Books work's cover belongs beside its media (issue #406): the folder of the EPUB/PDF
/// Jularr kept on the configured Books NAS library root (<c>MediaAcquisitionKind.Book</c>,
/// #389/#545), not Jularr's own <c>/data/books/files</c> copy. A book without a NAS-resolved
/// storage path (no library root configured, or not yet re-imported/refreshed since one was) has
/// no beside-media folder; its cover keeps using <c>/data/books/covers</c> until its next refresh.
/// </summary>
public static class BookArtworkFolders
{
    public static string? Resolve(string? libraryRoot, string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot) || string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        string root;
        string? directory;
        try
        {
            root = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            directory = Path.GetDirectoryName(Path.GetFullPath(storagePath));
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (directory is null || !Directory.Exists(directory))
        {
            return null;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        return string.Equals(directory, root, StringComparison.OrdinalIgnoreCase) ||
               directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? directory
            : null;
    }
}
