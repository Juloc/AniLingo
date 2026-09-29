using Jularr.Web.Features.Naming;

namespace Jularr.Web.Features.Audiobooks;

/// <summary>
/// Audiobook library naming (#440): an audiobook is placed in its own folder, under an author folder
/// when the author is known, as <c>Author/Title (Year)/…</c>. A single-file audiobook (one M4B) is
/// named <c>Title (Year).ext</c>; a multi-file (chaptered MP3) audiobook keeps each part's own file
/// name inside that folder so the parts never collide. Names are sanitised through the shared
/// <see cref="NamingTemplateEngine"/> so they are safe on every filesystem/SMB share. Configurable
/// naming profiles are a later follow-up; these are the built-in defaults.
/// </summary>
public static class AudiobookNaming
{
    /// <summary>The folder an audiobook's files are placed in, e.g. <c>Frank Herbert/Dune (1965)</c>.</summary>
    public static string FolderPath(string? author, string title, int? year)
    {
        var titleFolder = Compose(title, year);
        var authorFolder = string.IsNullOrWhiteSpace(author)
            ? null
            : NamingTemplateEngine.CleanFileName(author!) is { Length: > 0 } clean ? clean : null;
        return authorFolder is null ? titleFolder : Path.Combine(authorFolder, titleFolder);
    }

    /// <summary>The single-file audiobook file name, e.g. <c>Dune (1965).m4b</c> (the source extension is kept).</summary>
    public static string SingleFileName(string title, int? year, string extension) =>
        Compose(title, year) + NormalizeExtension(extension);

    private static string Compose(string title, int? year)
    {
        var clean = NamingTemplateEngine.CleanFileName(title ?? "");
        if (clean.Length == 0)
        {
            clean = "Untitled";
        }

        return year is int value and > 0 ? $"{clean} ({value})" : clean;
    }

    internal static string NormalizeExtension(string extension)
    {
        var trimmed = (extension ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
