using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// Seeds canonical Learning cards for catalog terms the way a profile that
/// already studied them would have them.
/// </summary>
internal static class LearningTestData
{
    public static async Task<LearningCard> SeedTermCardAsync(
        AppDbContext db,
        string profileId,
        Term term,
        UserTermState state,
        DateTime? nextReviewAt = null,
        DateTime? learningStartedAt = null,
        long? queuePosition = null,
        int intervalDays = 0,
        DateTime? updatedAt = null)
    {
        // Persist pending catalog rows (terms, episodes, ...) added by the test.
        await db.SaveChangesAsync();

        var store = new LearningCourseStore(db);
        var course = await store.ResolvePrimaryCourseAsync(
            profileId,
            term.Language,
            CancellationToken.None);
        var unit = await store.EnsureTermUnitAsync(term, CancellationToken.None);
        var cards = await store.EnsureCourseCardsAsync(
            profileId,
            course.Id,
            unit.Id,
            CancellationToken.None);
        var recognitionId = cards.Single(x => x.Mode == LearningCardMode.Recognition).Id;

        var card = await db.LearningCards.SingleAsync(x => x.Id == recognitionId);
        card.State = state;
        card.NextReviewAt = nextReviewAt;
        card.LearningStartedAt = learningStartedAt;
        card.QueuePosition = queuePosition;
        card.IntervalDays = intervalDays;
        card.UpdatedAt = updatedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
        return card;
    }

    public static LearningService Service(AppDbContext db, string profileId) =>
        new(db, new FsrsReviewScheduler(), TestAccounts.Context(profileId));

    public static LearningCardReview Review(
        string profileId,
        Guid cardId,
        DateTime reviewedAt,
        ReviewRating rating = ReviewRating.Good) =>
        new()
        {
            ProfileId = profileId,
            CardId = cardId,
            Rating = rating,
            ReviewedAt = reviewedAt,
            NextReviewAt = reviewedAt.AddDays(1)
        };
}

internal static class TestAccounts
{
    public static CurrentAccountContext Context(string profileId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, profileId),
            new Claim(ClaimTypes.Name, profileId),
            new Claim(ClaimTypes.Role, AccountRoles.User)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

        return new CurrentAccountContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        });
    }
}
