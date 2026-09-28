namespace Jularr.Web.Features.Naming;

/// <summary>
/// Renders reading naming templates (Books, Manga, Light Novels) with <see cref="NamingTemplateEngine"/>.
/// docs/INFORMATION_ARCHITECTURE.md §5.3 and docs/READING_ACQUISITION.md document where profiles
/// apply; this class only knows how to turn one <see cref="ReadingNamingRequest"/> into a folder
/// or file name.
/// </summary>
public static class ReadingNamingFormatter
{
    private static readonly HashSet<string> SeriesTokens = new(StringComparer.Ordinal)
    {
        "series", "author", "language"
    };

    private static readonly HashSet<string> FileOnlyTokens = new(StringComparer.Ordinal)
    {
        "title", "volume title", "volume", "volume number", "chapter", "chapter number", "format", "original title"
    };

    public static string BuildSeriesFolderName(ReadingNamingProfile profile, ReadingNamingRequest request) =>
        Render(profile.SeriesFolderFormat, ReadingNamingScope.SeriesFolder, request, errors: null);

    public static string BuildFileName(ReadingNamingProfile profile, ReadingNamingRequest request) =>
        Render(profile.FileFormat, ReadingNamingScope.File, request, errors: null);

    public static IReadOnlyList<string> Validate(ReadingNamingProfile profile, ReadingNamingRequest sample)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sample);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80)
        {
            errors.Add("Profile name must be 1-80 characters.");
        }

        ValidateTemplate("Series/work folder format", profile.SeriesFolderFormat, ReadingNamingScope.SeriesFolder, sample, errors);
        ValidateTemplate("File format", profile.FileFormat, ReadingNamingScope.File, sample, errors);

        return errors;
    }

    private static void ValidateTemplate(
        string label,
        string? template,
        ReadingNamingScope scope,
        ReadingNamingRequest sample,
        List<string> errors)
    {
        errors.AddRange(NamingTemplateEngine.ValidateLiteral(label, template));
        if (string.IsNullOrWhiteSpace(template) || template!.Length > 500)
        {
            return;
        }

        var tokenErrors = new List<string>();
        var rendered = Render(template, scope, sample, tokenErrors);
        errors.AddRange(tokenErrors.Distinct(StringComparer.Ordinal).Select(error => $"{label}: {error}"));

        if (tokenErrors.Count == 0 && string.IsNullOrWhiteSpace(rendered))
        {
            errors.Add($"{label} renders an empty name.");
        }
    }

    private static string Render(
        string template,
        ReadingNamingScope scope,
        ReadingNamingRequest request,
        List<string>? errors) =>
        NamingTemplateEngine.Render(
            template,
            (key, pad, tokenErrors) => ResolveToken(key, pad, scope, request, tokenErrors),
            errors);

    private static string? ResolveToken(
        string key,
        int pad,
        ReadingNamingScope scope,
        ReadingNamingRequest request,
        List<string>? errors)
    {
        var numeric = key is "volume" or "volume number" or "chapter" or "chapter number";
        if (!numeric && pad > 0)
        {
            errors?.Add($"{{{key}}} does not accept a format.");
            return null;
        }

        if (SeriesTokens.Contains(key))
        {
            return key switch
            {
                "series" => request.Series ?? "",
                "author" => request.Author ?? "",
                "language" => request.Language ?? "",
                _ => ""
            };
        }

        if (scope != ReadingNamingScope.File || !FileOnlyTokens.Contains(key))
        {
            errors?.Add(IsKnownToken(key)
                ? $"{{{key}}} is not available here."
                : $"{{{key}}} is not a supported token.");
            return null;
        }

        return key switch
        {
            "title" => request.Title ?? request.VolumeTitle ?? "",
            "volume title" => request.VolumeTitle ?? "",
            "volume" => request.VolumeNumber is int number ? $"Volume {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "",
            "volume number" => request.VolumeNumber is int volumeNumber ? NamingTemplateEngine.Pad(volumeNumber, pad) : "",
            "chapter" => request.ChapterNumber is double chapter ? $"Chapter {FormatChapter(chapter)}" : "",
            "chapter number" => request.ChapterNumber is double chapterNumber ? NamingTemplateEngine.PadDecimal(chapterNumber, pad) : "",
            "format" => request.Format ?? "",
            "original title" => request.OriginalFileName ?? "",
            _ => ""
        };
    }

    private static string FormatChapter(double value) =>
        NamingTemplateEngine.PadDecimal(value, 0);

    private static bool IsKnownToken(string key) =>
        SeriesTokens.Contains(key) || FileOnlyTokens.Contains(key);
}
