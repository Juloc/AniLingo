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

public sealed class LearningPreferences
{
    public const double DefaultDesiredRetention = 0.90;
    public const int DefaultReviewBatchSize = 50;

    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public double DesiredRetention { get; set; } = DefaultDesiredRetention;
    public int ReviewBatchSize { get; set; } = DefaultReviewBatchSize;
}

public sealed record LearningPreferencesSnapshot(
    double DesiredRetention,
    int ReviewBatchSize)
{
    public static LearningPreferencesSnapshot Default { get; } =
        new(LearningPreferences.DefaultDesiredRetention, LearningPreferences.DefaultReviewBatchSize);
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
        ReviewRating rating,
        double desiredRetention = LearningPreferences.DefaultDesiredRetention);

    IReadOnlyDictionary<ReviewRating, ReviewSchedule> Preview(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history,
        double desiredRetention = LearningPreferences.DefaultDesiredRetention);
}

public sealed class FsrsReviewScheduler : IReviewScheduler
{
    public ReviewSchedule Schedule(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history,
        ReviewRating rating,
        double desiredRetention = LearningPreferences.DefaultDesiredRetention)
    {
        var scheduler = CreateScheduler(desiredRetention);
        var card = Replay(scheduler, cardId, history, now);
        return ToSchedule(now, scheduler.ReviewCard(card, ToFsrsRating(rating), now).Card.Due);
    }

    public IReadOnlyDictionary<ReviewRating, ReviewSchedule> Preview(
        Guid cardId,
        DateTimeOffset now,
        IReadOnlyList<ReviewHistoryItem> history,
        double desiredRetention = LearningPreferences.DefaultDesiredRetention)
    {
        var scheduler = CreateScheduler(desiredRetention);
        var card = Replay(scheduler, cardId, history, now);

        return Enum.GetValues<ReviewRating>()
            .ToDictionary(
                rating => rating,
                rating => ToSchedule(
                    now,
                    scheduler.ReviewCard(card, ToFsrsRating(rating), now).Card.Due));
    }

    private static Scheduler CreateScheduler(double desiredRetention) =>
        new(new FsrsConfig
        {
            DesiredRetention = desiredRetention,
            MaximumInterval = 36500,
            EnableFuzzing = false,
            LearningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)],
            RelearningSteps = [TimeSpan.FromMinutes(10)]
        });

    private static Card Replay(
        Scheduler scheduler,
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


public sealed record ReviewAnimeContext(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    int CueStartMs,
    string Sentence)
{
    public string TimestampLabel
    {
        get
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, CueStartMs));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        }
    }
}
