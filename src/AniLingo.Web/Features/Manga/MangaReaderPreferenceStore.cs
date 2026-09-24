using AniLingo.Web.Data;
using AniLingo.Web.Features.ReaderPreferences;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Manga;

public sealed record MangaReaderPreset(
    string ReadingMode,
    bool TwoPageSpread,
    string PageTransition,
    string BookmarkColor,
    bool HasSeriesOverride)
{
    public string UiMode =>
        ReadingMode == "continuous"
            ? "continuous"
            : TwoPageSpread
                ? "double"
                : "single";
}

public static class MangaReaderPreferenceStore
{
    private const string MediaScope = "media:manga";

    public static async Task<MangaReaderPreset> GetAsync(
        AppDbContext db,
        string profileId,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var seriesScope = SeriesScope(seriesId);
        var preferences = await db.ReaderPreferences
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId &&
                (x.ScopeKey == ReaderPreferenceRules.UserDefaultScope ||
                 x.ScopeKey == MediaScope ||
                 x.ScopeKey == seriesScope))
            .ToListAsync(cancellationToken);

        var user = preferences.FirstOrDefault(
            x => x.ScopeKey == ReaderPreferenceRules.UserDefaultScope);
        var media = preferences.FirstOrDefault(x => x.ScopeKey == MediaScope);
        var series = preferences.FirstOrDefault(x => x.ScopeKey == seriesScope);

        var readingMode = ReaderPreferenceRules.NormalizeReadingMode(
            First(
                series?.ReadingMode,
                media?.ReadingMode,
                user?.ReadingMode,
                "paged"));
        var transition = ReaderPreferenceRules.NormalizePageTransition(
            First(
                series?.PageTransition,
                media?.PageTransition,
                user?.PageTransition,
                "slide"));
        var twoPageSpread =
            series?.TwoPageSpread
            ?? media?.TwoPageSpread
            ?? user?.TwoPageSpread
            ?? true;
        var bookmarkColor = ReaderPreferenceRules.NormalizeBookmarkColor(
            First(
                series?.BookmarkColor,
                media?.BookmarkColor,
                user?.BookmarkColor,
                "#b04455"));

        return new MangaReaderPreset(
            readingMode,
            twoPageSpread,
            transition,
            bookmarkColor,
            series is not null);
    }

    public static async Task SaveModeAsync(
        AppDbContext db,
        string profileId,
        Guid? seriesId,
        string? uiMode,
        CancellationToken cancellationToken)
    {
        var normalizedUiMode = uiMode?.Trim().ToLowerInvariant() switch
        {
            "continuous" => "continuous",
            "double" => "double",
            _ => "single"
        };

        var scope = seriesId is Guid id
            ? SeriesScope(id)
            : MediaScope;

        var preference = await db.ReaderPreferences
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.ScopeKey == scope,
                cancellationToken);

        if (preference is null)
        {
            preference = new ReaderPreference
            {
                ProfileId = profileId,
                ScopeKey = scope,
                WorkId = null
            };
            db.ReaderPreferences.Add(preference);
        }

        preference.ReadingMode =
            normalizedUiMode == "continuous" ? "continuous" : "paged";
        preference.TwoPageSpread = normalizedUiMode == "double";
        preference.PageTransition ??= "slide";
        preference.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task ResetSeriesAsync(
        AppDbContext db,
        string profileId,
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var scope = SeriesScope(seriesId);
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

    private static string SeriesScope(Guid seriesId) =>
        $"media:manga:series:{seriesId:N}";

    private static string First(params string?[] values) =>
        values.First(x => !string.IsNullOrWhiteSpace(x))!;
}
