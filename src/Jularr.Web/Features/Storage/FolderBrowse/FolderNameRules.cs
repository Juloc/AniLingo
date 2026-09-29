using System.Globalization;
using System.Text;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// What a new folder may be called. Names are trimmed (the one sanitizing step) and then either
/// accepted as written or refused: a name is never silently rewritten. The rules are the common
/// denominator of ext4, SMB shares and NTFS, because a library folder is often exported to a NAS.
/// </summary>
public static class FolderNameRules
{
    /// <summary>The longest file name every common file system accepts, in UTF-8 bytes.</summary>
    public const int MaxBytes = 255;

    private const string ForbiddenCharacters = "/\\:*?\"<>|";

    public static FolderNameProblem Validate(string? name, out string cleaned)
    {
        cleaned = name?.Trim() ?? string.Empty;
        if (cleaned.Length == 0)
        {
            return FolderNameProblem.Empty;
        }

        if (cleaned is "." or "..")
        {
            return FolderNameProblem.Reserved;
        }

        if (cleaned.Any(IsForbidden))
        {
            return FolderNameProblem.InvalidCharacter;
        }

        if (cleaned.EndsWith('.'))
        {
            return FolderNameProblem.EdgeCharacter;
        }

        return Encoding.UTF8.GetByteCount(cleaned) > MaxBytes
            ? FolderNameProblem.TooLong
            : FolderNameProblem.None;
    }

    private static bool IsForbidden(char character) =>
        ForbiddenCharacters.Contains(character)
        || char.IsControl(character)
        // Format characters (for example the right-to-left override) make a name read as another one.
        || CharUnicodeInfo.GetUnicodeCategory(character) is
            UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
