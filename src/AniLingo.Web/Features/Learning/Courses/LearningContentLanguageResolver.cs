using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning.Courses;

/// <summary>
/// Resolves the single content language that subtitle acquisition targets: the
/// language spoken in the anime dialogue that Learning courses are teaching
/// comprehension of. This is the only place that answers that question; every
/// acquisition step (<c>SubtitleImportService</c>, <c>SubtitleSidecarLocator</c>,
/// <c>EmbeddedSubtitleExtractor</c>) calls it instead of assuming Japanese.
/// </summary>
/// <remarks>
/// Media files and subtitle tracks are shared across every profile (they are
/// not partitioned per profile), so acquisition needs one answer even when
/// profiles disagree. The rule, in order:
/// <list type="number">
/// <item>Consider every enabled, primary <see cref="LearningCourse"/> across
/// every profile: a primary course is the one its profile designated to
/// receive catalog words from content in that source language, so its
/// <see cref="LearningCourse.SourceLanguage"/> is the language that profile
/// is acquiring subtitles to learn from.</item>
/// <item>Group by <see cref="LearningCourse.SourceLanguage"/>. The language
/// used by the most such courses wins.</item>
/// <item>Ties break on the language whose oldest matching course was created
/// first (the language the household has been using longest), and finally on
/// ordinal string comparison of the tag so the result is fully deterministic.</item>
/// <item>With no enabled primary course anywhere (a fresh install, or nobody
/// has configured Learning yet), the canonical default is Japanese.</item>
/// </list>
/// </remarks>
public sealed class LearningContentLanguageResolver(AppDbContext db)
{
    public const string DefaultLanguage = "ja";

    public async Task<string> ResolveTargetLanguageAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.LearningCourses
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.IsPrimary)
            .Select(x => new { x.SourceLanguage, x.CreatedAt })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return DefaultLanguage;
        }

        var winner = candidates
            .GroupBy(x => x.SourceLanguage, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Min(x => x.CreatedAt))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First()
            .Key;

        return LearningLanguageTag.TryNormalize(winner, out var normalized)
            ? normalized
            : DefaultLanguage;
    }
}
