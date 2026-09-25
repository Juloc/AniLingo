using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Learn;

public sealed class VocabularyModel(
    AppDbContext db,
    LearningService learningService,
    CurrentAccountContext currentAccount) : PageModel
{
    private const int PageSize = 250;

    public IReadOnlyList<VocabularyRow> Items { get; private set; } = [];
    public IReadOnlyList<LearningCourseSnapshot> Courses { get; private set; } = [];
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public string? Search { get; private set; }
    public string StateFilter { get; private set; } = "all";
    public Guid? CourseFilter { get; private set; }

    public async Task OnGetAsync(
        string? search,
        string? state,
        Guid? course,
        CancellationToken cancellationToken)
    {
        Ui = await new UiTranslationCatalogStore(db).LoadProfileBundleAsync(
            currentAccount.ProfileId,
            cancellationToken);

        Courses = await new LearningCourseStore(db).ListAsync(
            currentAccount.ProfileId,
            cancellationToken);
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        StateFilter = NormalizeFilter(state);
        CourseFilter = Courses.Any(x => x.Id == course) ? course : null;

        var query = LearningQueries.WordCards(db, currentAccount.ProfileId).AsNoTracking();

        if (TryParseState(StateFilter, out var parsedState))
        {
            query = query.Where(x => x.State == parsedState);
        }

        if (CourseFilter is { } courseId)
        {
            query = query.Where(x => x.CourseId == courseId);
        }

        if (Search is { } text)
        {
            query = query.Where(card => db.LearningVariants.Any(variant =>
                variant.UnitId == card.UnitId
                && (variant.Text.Contains(text)
                    || (variant.Reading != null && variant.Reading.Contains(text)))));
        }

        var wordCards = await query
            .OrderBy(x =>
                x.State == UserTermState.Learning ? 0 :
                x.State == UserTermState.Saved ? 1 :
                x.State == UserTermState.Known ? 2 :
                x.State == UserTermState.Suspended ? 3 : 4)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        var unitIds = wordCards.Select(x => x.UnitId).Distinct().ToArray();
        var courseIds = wordCards.Select(x => x.CourseId).Distinct().ToArray();

        var variants = (await db.LearningVariants
                .AsNoTracking()
                .Where(x => unitIds.Contains(x.UnitId))
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.UnitId);

        var courseCards = (await db.LearningCards
                .AsNoTracking()
                .Where(x =>
                    courseIds.Contains(x.CourseId)
                    && unitIds.Contains(x.UnitId))
                .ToListAsync(cancellationToken))
            .ToLookup(x => (x.CourseId, x.UnitId));

        var coursesById = Courses.ToDictionary(x => x.Id);
        var now = DateTime.UtcNow;

        Items = wordCards
            .Select(card =>
            {
                var course = coursesById[card.CourseId];
                var cards = courseCards[(card.CourseId, card.UnitId)].ToArray();
                var prompt = Pick(variants[card.UnitId], card.PromptLanguage);
                var answer = Pick(variants[card.UnitId], card.AnswerLanguage);

                return new VocabularyRow(
                    card.CourseId,
                    card.UnitId,
                    course.Name,
                    card.PromptLanguage,
                    prompt?.Text ?? "",
                    prompt?.Reading,
                    answer?.Text,
                    card.State,
                    cards.Any(x =>
                        x.State == UserTermState.Learning
                        && x.NextReviewAt != null
                        && x.NextReviewAt <= now),
                    cards
                        .Where(x => x.Mode != LearningCardMode.Recognition)
                        .OrderBy(x => x.Mode)
                        .Select(x => new VocabularyCardState(x.Mode, x.State))
                        .ToArray());
            })
            .ToArray();
    }

    public Task<IActionResult> OnPostSaveAsync(
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken) =>
        ChangeAsync(courseId, unitId, UserTermState.Saved, cancellationToken);

    public Task<IActionResult> OnPostLearnAsync(
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken) =>
        ChangeAsync(courseId, unitId, UserTermState.Learning, cancellationToken);

    public Task<IActionResult> OnPostKnownAsync(
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken) =>
        ChangeAsync(courseId, unitId, UserTermState.Known, cancellationToken);

    public Task<IActionResult> OnPostIgnoreAsync(
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken) =>
        ChangeAsync(courseId, unitId, UserTermState.Ignored, cancellationToken);

    public Task<IActionResult> OnPostSuspendAsync(
        Guid courseId,
        Guid unitId,
        CancellationToken cancellationToken) =>
        ChangeAsync(courseId, unitId, UserTermState.Suspended, cancellationToken);

    public string ModeLabel(LearningCardMode mode) =>
        mode switch
        {
            LearningCardMode.Production => Ui["learn.mode.production"],
            LearningCardMode.Listening => Ui["learn.mode.listening"],
            LearningCardMode.Writing => Ui["learn.mode.writing"],
            _ => Ui["learn.mode.recognition"]
        };

    private async Task<IActionResult> ChangeAsync(
        Guid courseId,
        Guid unitId,
        UserTermState state,
        CancellationToken cancellationToken)
    {
        var inCourse = await db.LearningCards
            .AsNoTracking()
            .AnyAsync(
                x => x.ProfileId == currentAccount.ProfileId
                    && x.CourseId == courseId
                    && x.UnitId == unitId,
                cancellationToken);
        if (!inCourse)
        {
            return NotFound();
        }

        await learningService.SetUnitStateAsync(
            courseId,
            unitId,
            state,
            cancellationToken);

        TempData["Status"] = $"Vocabulary item moved to {state}.";
        return RedirectToPage();
    }

    private static LearningVariant? Pick(
        IEnumerable<LearningVariant> variants,
        string languageTag) =>
        variants
            .Where(x => x.LanguageTag == languageTag)
            .OrderBy(x => x.Role == LearningVariantRole.Primary ? 0 : 1)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefault();

    private static string NormalizeFilter(string? state) =>
        TryParseState(state, out var parsed)
            ? parsed.ToString().ToLowerInvariant()
            : "all";

    private static bool TryParseState(
        string? value,
        out UserTermState state)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse<UserTermState>(
                value,
                ignoreCase: true,
                out state))
        {
            return true;
        }

        state = default;
        return false;
    }
}

public sealed record VocabularyCardState(
    LearningCardMode Mode,
    UserTermState State);

public sealed record VocabularyRow(
    Guid CourseId,
    Guid UnitId,
    string CourseName,
    string Language,
    string Text,
    string? Reading,
    string? Meaning,
    UserTermState State,
    bool Due,
    IReadOnlyList<VocabularyCardState> OtherCards);
