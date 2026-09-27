using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.Books;

/// <summary>
/// The book file formats Jularr imports. EPUB is preferred, PDF is accepted as it is and never
/// converted. Both become the same kind of Books work; only the stored file and the
/// <see cref="NovelWork.Format"/> flag (<c>EPUB:lang</c> or <c>PDF:lang</c>) tell them apart.
/// </summary>
public static class BookFileFormats
{
    public const string Epub = "EPUB";
    public const string Pdf = "PDF";
    public const string PdfMediaType = "application/pdf";

    public static string? FromPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".epub" => Epub,
            ".pdf" => Pdf,
            _ => null
        };

    public static bool IsSupported(string path) => FromPath(path) is not null;

    /// <summary>Whether a Books work was imported from a PDF (its <see cref="NovelWork.Format"/> says so).</summary>
    public static bool IsPdf(NovelWork work) => IsPdfFormat(work.Format);

    public static bool IsPdfFormat(string? workFormat) =>
        workFormat is not null && workFormat.StartsWith(Pdf + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The source language a Books work format carries (<c>EPUB:de</c>, <c>PDF:en</c>), or null.</summary>
    public static string? Language(string? workFormat)
    {
        var separator = workFormat?.IndexOf(':') ?? -1;
        if (separator < 0
            || !(workFormat![..separator].Equals(Epub, StringComparison.OrdinalIgnoreCase)
                || workFormat[..separator].Equals(Pdf, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var language = workFormat[(separator + 1)..].Trim().ToLowerInvariant();
        return language.Length > 0 ? language : null;
    }

    /// <summary>
    /// Chooses the files to import from what was found. A completed download is one book, so
    /// its EPUBs win over its PDFs; elsewhere (the inbox) a PDF is only skipped when an EPUB
    /// with the same name lies next to it.
    /// </summary>
    public static IReadOnlyList<string> Select(IEnumerable<string> files, bool singleBook)
    {
        var supported = files
            .Where(IsSupported)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (singleBook)
        {
            var epubs = supported.Where(file => FromPath(file) == Epub).ToArray();
            return epubs.Length > 0 ? epubs : supported;
        }

        var epubStems = supported
            .Where(file => FromPath(file) == Epub)
            .Select(Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return supported
            .Where(file => FromPath(file) == Epub || !epubStems.Contains(Stem(file)))
            .ToArray();
    }

    private static string Stem(string file) =>
        Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file));
}
