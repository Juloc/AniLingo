using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace AniLingo.Web.Features.Learning;

public enum UserTermState
{
    Known = 1,
    Learning = 2
}

public enum ReviewRating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4
}

public static class LearningProfile
{
    public const string DefaultId = "default";
}

public sealed class UserTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid TermId { get; set; }
    public UserTermState State { get; set; }
    public int IntervalDays { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Review
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid TermId { get; set; }
    public ReviewRating Rating { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextReviewAt { get; set; }
}

public sealed record ReviewHistoryItem(ReviewRating Rating, DateTimeOffset ReviewedAt);

public sealed record ReviewSchedule(DateTimeOffset NextReviewAt, int IntervalDays);

public sealed record ReviewOption(ReviewRating Rating, DateTimeOffset NextReviewAt, string IntervalLabel);

public interface IReviewScheduler
{
    ReviewSchedule Schedule(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history,
        ReviewRating rating);

    IReadOnlyDictionary<ReviewRating, ReviewSchedule> Preview(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history);
}

public sealed class FsrsReviewScheduler : IReviewScheduler
{
    private readonly Scheduler scheduler = new(new FsrsConfig
    {
        DesiredRetention = 0.90,
        MaximumInterval = 36500,
        EnableFuzzing = false,
        LearningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)],
        RelearningSteps = [TimeSpan.FromMinutes(10)]
    });

    public ReviewSchedule Schedule(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history,
        ReviewRating rating)
    {
        var card = Replay(cardId, history, now);
        return ToSchedule(now, scheduler.ReviewCard(card, ToFsrsRating(rating), now).Card.Due);
    }

    public IReadOnlyDictionary<ReviewRating, ReviewSchedule> Preview(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history)
    {
        var card = Replay(cardId, history, now);

        return Enum.GetValues<ReviewRating>()
            .ToDictionary(
                rating => rating,
                rating => ToSchedule(
                    now,
                    scheduler.ReviewCard(card, ToFsrsRating(rating), now).Card.Due));
    }

    private Card Replay(
        Guid cardId,
        IReadOnlyList<ReviewHistoryItem> history,
        DateTimeOffset now)
    {
        var card = new Card(
            cardId: cardId,
            state: State.New,
            due: history.Count > 0 ? history.Min(x => x.ReviewedAt) : now);

        foreach (var review in history.OrderBy(x => x.ReviewedAt))
        {
            card = scheduler.ReviewCard(
                card,
                ToFsrsRating(review.Rating),
                review.ReviewedAt).Card;
        }

        return card;
    }

    private static Rating ToFsrsRating(ReviewRating rating) =>
        rating switch
        {
            ReviewRating.Again => Rating.Again,
            ReviewRating.Hard => Rating.Hard,
            ReviewRating.Good => Rating.Good,
            ReviewRating.Easy => Rating.Easy,
            _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, null)
        };

    private static ReviewSchedule ToSchedule(DateTimeOffset now, DateTimeOffset due)
    {
        var interval = due - now;
        var days = interval < TimeSpan.FromDays(1)
            ? 0
            : Math.Max(1, (int)Math.Round(interval.TotalDays));

        return new ReviewSchedule(due, days);
    }
}

public sealed record DueReviewItem(
    Guid TermId,
    string Canonical,
    string? Reading,
    string? Meaning,
    int IntervalDays);
