using System.Text.Json;
using System.Text.RegularExpressions;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.ReaderPreferences;

public sealed class ReaderPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "";
    public string ScopeKey { get; set; } = "";
    public Guid? WorkId { get; set; }

    public string? ReadingMode { get; set; }
    public string? PageTransition { get; set; }
    public bool? TwoPageSpread { get; set; }
    public double? AutoScrollSpeed { get; set; }

    public string? FontFamily { get; set; }
    public double? FontSizeRem { get; set; }
    public double? LineHeight { get; set; }
    public double? ParagraphSpacingEm { get; set; }
    public int? TextWidthPx { get; set; }
    public string? TextAlignment { get; set; }

    public string? ChapterStyle { get; set; }
    public string? PaperStyle { get; set; }
    public bool? GenreArtworkEnabled { get; set; }
    public string? GenreTheme { get; set; }
    public string? BackgroundAssetId { get; set; }
    public double? BackgroundIntensity { get; set; }

    public string? BookmarkStyle { get; set; }
    public string? BookmarkColor { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ReaderSettingsInput
{
    public string? ReadingMode { get; set; }
    public string? PageTransition { get; set; }
    public bool TwoPageSpread { get; set; }
    public double AutoScrollSpeed { get; set; }

    public string? FontFamily { get; set; }
    public double FontSizeRem { get; set; }
    public double LineHeight { get; set; }
    public double ParagraphSpacingEm { get; set; }
    public int TextWidthPx { get; set; }
    public string? TextAlignment { get; set; }

    public string? ChapterStyle { get; set; }
    public string? PaperStyle { get; set; }
    public bool GenreArtworkEnabled { get; set; }
    public string? GenreTheme { get; set; }
    public string? BackgroundAssetId { get; set; }
    public double BackgroundIntensity { get; set; }

    public string? BookmarkStyle { get; set; }
    public string? BookmarkColor { get; set; }
}

public sealed record ReaderSettingsSnapshot(
    string ReadingMode,
    string PageTransition,
    bool TwoPageSpread,
    double AutoScrollSpeed,
    string FontFamily,
    double FontSizeRem,
    double LineHeight,
    double ParagraphSpacingEm,
    int TextWidthPx,
    string TextAlignment,
    string ChapterStyle,
    string PaperStyle,
    bool GenreArtworkEnabled,
    string GenreTheme,
    string ResolvedGenreTheme,
    string BackgroundAssetId,
    double BackgroundIntensity,
    string BookmarkStyle,
    string BookmarkColor,
    bool HasBookOverride);

public static partial class ReaderPreferenceRules
{
    public const string UserDefaultScope = "default";

    private static readonly HashSet<string> ReadingModes =
        new(StringComparer.OrdinalIgnoreCase) { "continuous", "paged" };
    private static readonly HashSet<string> PageTransitions =
        new(StringComparer.OrdinalIgnoreCase) { "curl", "slide", "fade", "none" };
    private static readonly HashSet<string> TextAlignments =
        new(StringComparer.OrdinalIgnoreCase) { "start", "justify" };
    private static readonly HashSet<string> ChapterStyles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "classic", "modern", "light-novel", "minimal", "decorative"
        };
    private static readonly HashSet<string> PaperStyles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "white", "cream", "sepia", "old-paper", "midnight", "oled"
        };
    private static readonly HashSet<string> GenreThemes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "auto", "neutral", "horror", "romance", "fantasy", "sci-fi",
            "mystery", "adventure", "slice-of-life", "psychological",
            "dark-fantasy", "urban-fantasy", "isekai", "supernatural", "gothic"
        };
    private static readonly HashSet<string> BookmarkStyles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "fabric", "paper", "leather", "cord", "minimal"
        };
    private static readonly HashSet<string> BuiltInFonts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "system-serif", "system-sans", "literary-serif", "book-serif",
            "atkinson", "noto-serif-jp", "noto-sans-jp"
        };

    public static string NormalizeReadingMode(string? value) =>
        NormalizeChoice(value, ReadingModes, "continuous");

    public static string NormalizePageTransition(string? value) =>
        NormalizeChoice(value, PageTransitions, "curl");

    public static string NormalizeTextAlignment(string? value) =>
        NormalizeChoice(value, TextAlignments, "start");

    public static string NormalizeChapterStyle(string? value) =>
        NormalizeChoice(value, ChapterStyles, "light-novel");

    public static string NormalizePaperStyle(string? value) =>
        NormalizeChoice(value, PaperStyles, "midnight");

    public static string NormalizeGenreTheme(string? value) =>
        NormalizeChoice(value, GenreThemes, "auto");

    public static string NormalizeBookmarkStyle(string? value) =>
        NormalizeChoice(value, BookmarkStyles, "fabric");

    public static string NormalizeBackgroundAssetId(string? value)
    {
        var normalized = value?.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return "auto";
        }

        return normalized.Length <= 120 && BackgroundAssetId().IsMatch(normalized)
            ? normalized.ToLowerInvariant()
            : "auto";
    }

    public static string NormalizeBookmarkColor(string? value)
    {
        var normalized = value?.Trim();
        return normalized is not null && HexColor().IsMatch(normalized)
            ? normalized.ToLowerInvariant()
            : "#b04455";
    }

    public static string NormalizeFontFamily(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "literary-serif";
        }

        if (BuiltInFonts.Contains(normalized))
        {
            return normalized.ToLowerInvariant();
        }

        if (normalized.StartsWith("google:", StringComparison.OrdinalIgnoreCase))
        {
            var family = normalized["google:".Length..].Trim();
            if (family.Length is > 0 and <= 80 && GoogleFontName().IsMatch(family))
            {
                return $"google:{family}";
            }
        }

        return "literary-serif";
    }

    public static double NormalizeAutoScrollSpeed(double value) =>
        Math.Round(Math.Clamp(value, 10, 180), 1);

    public static double NormalizeFontSize(double value) =>
        Math.Round(Math.Clamp(value, .85, 1.8), 2);

    public static double NormalizeLineHeight(double value) =>
        Math.Round(Math.Clamp(value, 1.4, 2.6), 2);

    public static double NormalizeParagraphSpacing(double value) =>
        Math.Round(Math.Clamp(value, 0, 2.2), 2);

    public static int NormalizeTextWidth(int value) =>
        Math.Clamp(value, 520, 1100);

    public static double NormalizeBackgroundIntensity(double value) =>
        Math.Round(Math.Clamp(value, 0, .12), 3);

    public static IReadOnlyList<string> ParseGenres(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                ?.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string InferGenreTheme(IReadOnlyList<string> genres)
    {
        foreach (var genre in genres)
        {
            var key = genre.Trim().ToLowerInvariant();
            if (key.Contains("horror", StringComparison.Ordinal)) return "horror";
            if (key.Contains("romance", StringComparison.Ordinal)) return "romance";
            if (key.Contains("psychological", StringComparison.Ordinal)) return "psychological";
            if (key.Contains("supernatural", StringComparison.Ordinal)) return "supernatural";
            if (key.Contains("mystery", StringComparison.Ordinal) ||
                key.Contains("crime", StringComparison.Ordinal) ||
                key.Contains("detective", StringComparison.Ordinal)) return "mystery";
            if (key.Contains("sci-fi", StringComparison.Ordinal) ||
                key.Contains("science fiction", StringComparison.Ordinal)) return "sci-fi";
            if (key.Contains("adventure", StringComparison.Ordinal)) return "adventure";
            if (key.Contains("slice of life", StringComparison.Ordinal)) return "slice-of-life";
            if (key.Contains("fantasy", StringComparison.Ordinal)) return "fantasy";
            if (key.Contains("gothic", StringComparison.Ordinal)) return "gothic";
        }

        return "neutral";
    }

    private static string NormalizeChoice(
        string? value,
        HashSet<string> allowed,
        string fallback)
    {
        var normalized = value?.Trim();
        return normalized is not null && allowed.Contains(normalized)
            ? normalized.ToLowerInvariant()
            : fallback;
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_-]*/[a-zA-Z0-9][a-zA-Z0-9_/-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex BackgroundAssetId();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    [GeneratedRegex("^[\\p{L}\\p{N} ._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleFontName();
}

public static class ReaderPreferenceStore
{
    public static async Task<ReaderSettingsSnapshot> GetAsync(
        AppDbContext db,
        string profileId,
        Guid workId,
        string? genresJson,
        CancellationToken cancellationToken)
    {
        var workScope = WorkScope(workId);
        var preferences = await db.ReaderPreferences
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId &&
                (x.ScopeKey == ReaderPreferenceRules.UserDefaultScope ||
                 x.ScopeKey == workScope))
            .ToListAsync(cancellationToken);

        var user = preferences.FirstOrDefault(
            x => x.ScopeKey == ReaderPreferenceRules.UserDefaultScope);
        var book = preferences.FirstOrDefault(x => x.ScopeKey == workScope);
        var genres = ReaderPreferenceRules.ParseGenres(genresJson);

        var genreSelection = First(
            book?.GenreTheme,
            user?.GenreTheme,
            "auto");
        genreSelection = ReaderPreferenceRules.NormalizeGenreTheme(genreSelection);

        return new ReaderSettingsSnapshot(
            ReadingMode: ReaderPreferenceRules.NormalizeReadingMode(
                First(book?.ReadingMode, user?.ReadingMode, "continuous")),
            PageTransition: ReaderPreferenceRules.NormalizePageTransition(
                First(book?.PageTransition, user?.PageTransition, "curl")),
            TwoPageSpread: book?.TwoPageSpread ?? user?.TwoPageSpread ?? true,
            AutoScrollSpeed: ReaderPreferenceRules.NormalizeAutoScrollSpeed(
                book?.AutoScrollSpeed ?? user?.AutoScrollSpeed ?? 36),
            FontFamily: ReaderPreferenceRules.NormalizeFontFamily(
                First(book?.FontFamily, user?.FontFamily, "literary-serif")),
            FontSizeRem: ReaderPreferenceRules.NormalizeFontSize(
                book?.FontSizeRem ?? user?.FontSizeRem ?? 1.06),
            LineHeight: ReaderPreferenceRules.NormalizeLineHeight(
                book?.LineHeight ?? user?.LineHeight ?? 1.9),
            ParagraphSpacingEm: ReaderPreferenceRules.NormalizeParagraphSpacing(
                book?.ParagraphSpacingEm ?? user?.ParagraphSpacingEm ?? .85),
            TextWidthPx: ReaderPreferenceRules.NormalizeTextWidth(
                book?.TextWidthPx ?? user?.TextWidthPx ?? 760),
            TextAlignment: ReaderPreferenceRules.NormalizeTextAlignment(
                First(book?.TextAlignment, user?.TextAlignment, "start")),
            ChapterStyle: ReaderPreferenceRules.NormalizeChapterStyle(
                First(book?.ChapterStyle, user?.ChapterStyle, "light-novel")),
            PaperStyle: ReaderPreferenceRules.NormalizePaperStyle(
                First(book?.PaperStyle, user?.PaperStyle, "midnight")),
            GenreArtworkEnabled:
                book?.GenreArtworkEnabled ?? user?.GenreArtworkEnabled ?? true,
            GenreTheme: genreSelection,
            ResolvedGenreTheme: genreSelection == "auto"
                ? ReaderPreferenceRules.InferGenreTheme(genres)
                : genreSelection,
            BackgroundAssetId: ReaderPreferenceRules.NormalizeBackgroundAssetId(
                First(book?.BackgroundAssetId, user?.BackgroundAssetId, "auto")),
            BackgroundIntensity: ReaderPreferenceRules.NormalizeBackgroundIntensity(
                book?.BackgroundIntensity ?? user?.BackgroundIntensity ?? .055),
            BookmarkStyle: ReaderPreferenceRules.NormalizeBookmarkStyle(
                First(book?.BookmarkStyle, user?.BookmarkStyle, "fabric")),
            BookmarkColor: ReaderPreferenceRules.NormalizeBookmarkColor(
                First(book?.BookmarkColor, user?.BookmarkColor, "#b04455")),
            HasBookOverride: book is not null);
    }

    public static async Task SaveUserDefaultsAsync(
        AppDbContext db,
        string profileId,
        ReaderSettingsInput input,
        CancellationToken cancellationToken)
    {
        var preference = await FindOrCreateAsync(
            db,
            profileId,
            ReaderPreferenceRules.UserDefaultScope,
            null,
            cancellationToken);

        preference.ReadingMode =
            ReaderPreferenceRules.NormalizeReadingMode(input.ReadingMode);
        preference.PageTransition =
            ReaderPreferenceRules.NormalizePageTransition(input.PageTransition);
        preference.TwoPageSpread = input.TwoPageSpread;
        preference.AutoScrollSpeed =
            ReaderPreferenceRules.NormalizeAutoScrollSpeed(input.AutoScrollSpeed);
        preference.FontFamily =
            ReaderPreferenceRules.NormalizeFontFamily(input.FontFamily);
        preference.FontSizeRem =
            ReaderPreferenceRules.NormalizeFontSize(input.FontSizeRem);
        preference.LineHeight =
            ReaderPreferenceRules.NormalizeLineHeight(input.LineHeight);
        preference.ParagraphSpacingEm =
            ReaderPreferenceRules.NormalizeParagraphSpacing(input.ParagraphSpacingEm);
        preference.TextWidthPx =
            ReaderPreferenceRules.NormalizeTextWidth(input.TextWidthPx);
        preference.TextAlignment =
            ReaderPreferenceRules.NormalizeTextAlignment(input.TextAlignment);
        preference.ChapterStyle =
            ReaderPreferenceRules.NormalizeChapterStyle(input.ChapterStyle);
        preference.PaperStyle =
            ReaderPreferenceRules.NormalizePaperStyle(input.PaperStyle);
        preference.GenreArtworkEnabled = input.GenreArtworkEnabled;
        preference.GenreTheme =
            ReaderPreferenceRules.NormalizeGenreTheme(input.GenreTheme);
        preference.BackgroundAssetId =
            ReaderPreferenceRules.NormalizeBackgroundAssetId(input.BackgroundAssetId);
        preference.BackgroundIntensity =
            ReaderPreferenceRules.NormalizeBackgroundIntensity(input.BackgroundIntensity);
        preference.BookmarkStyle =
            ReaderPreferenceRules.NormalizeBookmarkStyle(input.BookmarkStyle);
        preference.BookmarkColor =
            ReaderPreferenceRules.NormalizeBookmarkColor(input.BookmarkColor);
        preference.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SaveBookOverrideAsync(
        AppDbContext db,
        string profileId,
        Guid workId,
        string changedKey,
        ReaderSettingsInput input,
        CancellationToken cancellationToken)
    {
        var preference = await FindOrCreateAsync(
            db,
            profileId,
            WorkScope(workId),
            workId,
            cancellationToken);

        switch (changedKey.Trim())
        {
            case "readingMode":
                preference.ReadingMode =
                    ReaderPreferenceRules.NormalizeReadingMode(input.ReadingMode);
                break;
            case "pageTransition":
                preference.PageTransition =
                    ReaderPreferenceRules.NormalizePageTransition(input.PageTransition);
                break;
            case "twoPageSpread":
                preference.TwoPageSpread = input.TwoPageSpread;
                break;
            case "autoScrollSpeed":
                preference.AutoScrollSpeed =
                    ReaderPreferenceRules.NormalizeAutoScrollSpeed(input.AutoScrollSpeed);
                break;
            case "fontFamily":
                preference.FontFamily =
                    ReaderPreferenceRules.NormalizeFontFamily(input.FontFamily);
                break;
            case "fontSizeRem":
                preference.FontSizeRem =
                    ReaderPreferenceRules.NormalizeFontSize(input.FontSizeRem);
                break;
            case "lineHeight":
                preference.LineHeight =
                    ReaderPreferenceRules.NormalizeLineHeight(input.LineHeight);
                break;
            case "paragraphSpacingEm":
                preference.ParagraphSpacingEm =
                    ReaderPreferenceRules.NormalizeParagraphSpacing(input.ParagraphSpacingEm);
                break;
            case "textWidthPx":
                preference.TextWidthPx =
                    ReaderPreferenceRules.NormalizeTextWidth(input.TextWidthPx);
                break;
            case "textAlignment":
                preference.TextAlignment =
                    ReaderPreferenceRules.NormalizeTextAlignment(input.TextAlignment);
                break;
            case "chapterStyle":
                preference.ChapterStyle =
                    ReaderPreferenceRules.NormalizeChapterStyle(input.ChapterStyle);
                break;
            case "paperStyle":
                preference.PaperStyle =
                    ReaderPreferenceRules.NormalizePaperStyle(input.PaperStyle);
                break;
            case "genreArtworkEnabled":
                preference.GenreArtworkEnabled = input.GenreArtworkEnabled;
                break;
            case "genreTheme":
                preference.GenreTheme =
                    ReaderPreferenceRules.NormalizeGenreTheme(input.GenreTheme);
                break;
            case "backgroundAssetId":
                preference.BackgroundAssetId =
                    ReaderPreferenceRules.NormalizeBackgroundAssetId(input.BackgroundAssetId);
                break;
            case "backgroundIntensity":
                preference.BackgroundIntensity =
                    ReaderPreferenceRules.NormalizeBackgroundIntensity(input.BackgroundIntensity);
                break;
            case "bookmarkStyle":
                preference.BookmarkStyle =
                    ReaderPreferenceRules.NormalizeBookmarkStyle(input.BookmarkStyle);
                break;
            case "bookmarkColor":
                preference.BookmarkColor =
                    ReaderPreferenceRules.NormalizeBookmarkColor(input.BookmarkColor);
                break;
            default:
                throw new InvalidOperationException("Unknown reader setting.");
        }

        preference.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ResetBookAsync(
        AppDbContext db,
        string profileId,
        Guid workId,
        CancellationToken cancellationToken)
    {
        var scope = WorkScope(workId);
        var preference = await db.ReaderPreferences
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.ScopeKey == scope,
                cancellationToken);

        if (preference is null)
        {
            return;
        }

        db.ReaderPreferences.Remove(preference);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ReaderPreference> FindOrCreateAsync(
        AppDbContext db,
        string profileId,
        string scopeKey,
        Guid? workId,
        CancellationToken cancellationToken)
    {
        var preference = await db.ReaderPreferences
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.ScopeKey == scopeKey,
                cancellationToken);

        if (preference is not null)
        {
            return preference;
        }

        preference = new ReaderPreference
        {
            ProfileId = profileId,
            ScopeKey = scopeKey,
            WorkId = workId
        };
        db.ReaderPreferences.Add(preference);
        return preference;
    }

    private static string WorkScope(Guid workId) => $"work:{workId:N}";

    private static string First(params string?[] values) =>
        values.First(x => !string.IsNullOrWhiteSpace(x))!;
}
