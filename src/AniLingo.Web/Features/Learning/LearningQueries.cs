using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning.Courses;

namespace AniLingo.Web.Features.Learning;

/// <summary>
/// Learning state of one catalog term for a profile: the Recognition card of
/// the term's unit in the profile's primary course for the term language.
/// </summary>
public sealed class LearningTermState
{
    public Guid TermId { get; init; }
    public Guid CardId { get; init; }
    public Guid CourseId { get; init; }
    public UserTermState State { get; init; }
    public DateTime? NextReviewAt { get; init; }
    public bool SentencePracticeEnabled { get; init; }
}

/// <summary>
/// Shared read models over the canonical Learning cards. Every surface that
/// counts, lists or schedules learning state composes these queries instead of
/// re-deriving course/mode rules.
/// </summary>
public static class LearningQueries
{
    /// <summary>
    /// Cards that may be scheduled or counted as reviews: the course is enabled
    /// and the card's practice mode is enabled for that course.
    /// </summary>
    public static IQueryable<LearningCard> ScheduledCards(
        AppDbContext db,
        string profileId) =>
        from card in db.LearningCards
        join course in db.LearningCourses on card.CourseId equals course.Id
        where card.ProfileId == profileId
            && course.IsEnabled
            && ((card.Mode == LearningCardMode.Recognition && course.RecognitionEnabled)
                || (card.Mode == LearningCardMode.Production && course.ProductionEnabled)
                || (card.Mode == LearningCardMode.Listening && course.ListeningEnabled)
                || (card.Mode == LearningCardMode.Writing && course.WritingEnabled))
        select card;

    public static IQueryable<LearningCard> DueCards(
        AppDbContext db,
        string profileId,
        DateTime nowUtc) =>
        ScheduledCards(db, profileId)
            .Where(x =>
                x.State == UserTermState.Learning
                && x.NextReviewAt != null
                && x.NextReviewAt <= nowUtc);

    /// <summary>
    /// Word cards of a profile, one per course and unit: the Recognition card is
    /// the unit's anchor and carries its Saved/Learning/Known state.
    /// </summary>
    public static IQueryable<LearningCard> WordCards(
        AppDbContext db,
        string profileId) =>
        db.LearningCards.Where(x =>
            x.ProfileId == profileId
            && x.Mode == LearningCardMode.Recognition);

    /// <summary>At most one row per catalog term.</summary>
    public static IQueryable<LearningTermState> TermStates(
        AppDbContext db,
        string profileId) =>
        from card in db.LearningCards
        join course in db.LearningCourses on card.CourseId equals course.Id
        join unit in db.LearningUnits on card.UnitId equals unit.Id
        join term in db.Terms on unit.TermId equals (Guid?)term.Id
        where card.ProfileId == profileId
            && card.Mode == LearningCardMode.Recognition
            && course.IsPrimary
            && course.SourceLanguage == term.Language
        select new LearningTermState
        {
            TermId = term.Id,
            CardId = card.Id,
            CourseId = course.Id,
            State = card.State,
            NextReviewAt = card.NextReviewAt,
            SentencePracticeEnabled = course.IsEnabled && course.SentencePracticeEnabled
        };
}
