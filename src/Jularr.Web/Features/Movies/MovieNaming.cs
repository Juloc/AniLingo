using Jularr.Web.Features.Naming;

namespace Jularr.Web.Features.Movies;

/// <summary>
/// Movie library naming (#593): a movie is a single unit placed in its own folder as
/// <c>Title (Year)/Title (Year).ext</c>. Names are sanitised through the shared
/// <see cref="NamingTemplateEngine"/> so they are byte-identical to the rest of Jularr's placement and
/// safe on every filesystem/SMB share. Configurable naming profiles are a later follow-up; these are the
/// built-in defaults.
/// </summary>
public static class MovieNaming
{
    /// <summary>The folder a movie's files are placed in, e.g. <c>Inception (2010)</c>.</summary>
    public static string FolderName(string title, int? year) => Compose(title, year);

    /// <summary>The movie file name, e.g. <c>Inception (2010).mkv</c> (the source extension is kept).</summary>
    public static string FileName(string title, int? year, string extension) =>
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
