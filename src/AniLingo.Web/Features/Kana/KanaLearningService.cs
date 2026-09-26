using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Kana;

/// <summary>
/// Kana practice on top of the canonical Learning model: every Kana symbol is a
/// Script unit with a stable ID, and the profile learns them in a non-primary
/// ja → ja-Latn course so catalog words are never saved into it implicitly.
/// </summary>
public sealed class KanaLearningService(
    AppDbContext db,
    LearningService learningService)
{
    public Task<Guid?> FindCourseAsync(CancellationToken cancellationToken)
    {
        var profileId = learningService.ProfileId;
        return db.LearningCourses
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId
                && x.SourceLanguage == KanaCatalog.PromptLanguage
                && x.TargetLanguage == KanaCatalog.AnswerLanguage)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Creates the Kana units and the profile's Kana course on first practice.</summary>
    public async Task<Guid> PrepareAsync(CancellationToken cancellationToken)
    {
        await EnsureUnitsAsync(cancellationToken);

        if (await FindCourseAsync(cancellationToken) is { } existing)
        {
            return existing;
        }

        var course = await new LearningCourseStore(db).CreateAsync(
            learningService.ProfileId,
            KanaCatalog.PromptLanguage,
            KanaCatalog.AnswerLanguage,
            KanaCatalog.CourseName,
            new LearningCourseOptions(SentencePracticeEnabled: false),
            primaryWhenFirstForSource: false,
            cancellationToken);

        return course.Id;
    }

    private async Task EnsureUnitsAsync(CancellationToken cancellationToken)
    {
        var ids = KanaCatalog.All.Select(KanaCatalog.IdFor).ToArray();
        var existing = (await db.LearningUnits
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = DateTime.UtcNow;
        var added = false;
        foreach (var entry in KanaCatalog.All)
        {
            var id = KanaCatalog.IdFor(entry);
            if (existing.Contains(id))
            {
                continue;
            }

            db.LearningUnits.Add(new LearningUnit
            {
                Id = id,
                Kind = LearningUnitKind.Script,
                CreatedAt = now
            });
            db.LearningVariants.AddRange(
                new LearningVariant
                {
                    UnitId = id,
                    LanguageTag = KanaCatalog.PromptLanguage,
                    Text = entry.Symbol,
                    SourceKind = LearningVariantSource.ScriptCatalog,
                    CreatedAt = now
                },
                new LearningVariant
                {
                    UnitId = id,
                    LanguageTag = KanaCatalog.AnswerLanguage,
                    Text = entry.Romaji,
                    SourceKind = LearningVariantSource.ScriptCatalog,
                    CreatedAt = now
                });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Kana cards of the profile keyed by Kana unit ID.</summary>
    public async Task<IReadOnlyDictionary<Guid, LearningCard>> LoadCardsAsync(
        Guid? courseId,
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken)
    {
        if (courseId is not { } id)
        {
            return new Dictionary<Guid, LearningCard>();
        }

        var profileId = learningService.ProfileId;
        return await db.LearningCards
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId
                && x.CourseId == id
                && x.Mode == LearningCardMode.Recognition
                && unitIds.Contains(x.UnitId))
            .ToDictionaryAsync(x => x.UnitId, cancellationToken);
    }

    /// <summary>
    /// Records a practice answer. Known Kana stay known; everything else joins
    /// active learning and is rated Good or Again.
    /// </summary>
    public async Task<UserTermState?> AnswerAsync(
        Guid courseId,
        Guid unitId,
        bool correct,
        CancellationToken cancellationToken)
    {
        var cards = await LoadCardsAsync(courseId, [unitId], cancellationToken);
        var previous = cards.GetValueOrDefault(unitId)?.State;
        if (previous == UserTermState.Known)
        {
            return previous;
        }

        await learningService.SetUnitStateAsync(
            courseId,
            unitId,
            UserTermState.Learning,
            cancellationToken);

        var card = (await LoadCardsAsync(courseId, [unitId], cancellationToken))[unitId];
        await learningService.ReviewAsync(
            card.Id,
            correct ? ReviewRating.Good : ReviewRating.Again,
            cancellationToken);

        return previous;
    }
}
