using AniLingo.Web.Features.Learning.Courses;
using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace AniLingo.Web.Features.Learning;

public enum UserTermState
{
    Known = 1,
    Learning = 2,
    Saved = 3,
    Ignored = 4,
    Suspended = 5
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
    public const int DefaultNewWordsPerDay = 10;

    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public double DesiredRetention { get; set; } = DefaultDesiredRetention;
    public int ReviewBatchSize { get; set; } = DefaultReviewBatchSize;
    public int NewWordsPerDay { get; set; } = DefaultNewWordsPerDay;
}

public sealed record LearningPreferencesSnapshot(
    double DesiredRetention,
    int ReviewBatchSize,
    int NewWordsPerDay)
{
    public static LearningPreferencesSnapshot Default { get; } =
        new(
            LearningPreferences.DefaultDesiredRetention,
            LearningPreferences.DefaultReviewBatchSize,
            LearningPreferences.DefaultNewWordsPerDay);
}

public sealed record ReviewHistoryItem(ReviewRating Rating, DateTimeOffset ReviewedAt);

public sealed record ReviewSchedule(DateTimeOffset NextReviewAt, int IntervalDays);

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

/// <summary>
/// One due directional card. Prompt and answer come from the unit variants in
/// the card's prompt/answer languages; the mode decides how the prompt is
/// presented (read, heard or written).
/// </summary>
public sealed record DueReviewItem(
    Guid CardId,
    Guid UnitId,
    Guid? TermId,
    LearningCardMode Mode,
    string PromptLanguage,
    string AnswerLanguage,
    string Prompt,
    string? PromptReading,
    string? Answer,
    string? AnswerReading,
    int IntervalDays);


/// <summary>
/// The BCP-47 tag of <see cref="Sentence"/> is the term's own source language,
/// never an assumed Japanese one: it is whatever language the subtitle track
/// the sentence came from actually carries.
/// </summary>
public sealed record ReviewAnimeContext(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string EpisodeTitle,
    int CueStartMs,
    string Sentence,
    string Language)
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


/// <summary>
/// Offline rating. <see cref="CardId"/> addresses one directional card.
/// A term-only event addresses the word's Recognition card in the primary
/// course for the term's language.
/// </summary>
public sealed record OfflineReviewEvent(
    Guid EventId,
    Guid? TermId,
    ReviewRating Rating,
    DateTime ReviewedAtUtc,
    Guid? CardId = null);

public sealed record OfflineReviewSyncRequest(
    IReadOnlyList<OfflineReviewEvent> Events);

public sealed record OfflineReviewSyncResult(
    IReadOnlyList<Guid> Accepted,
    IReadOnlyList<Guid> AlreadyApplied,
    IReadOnlyList<Guid> Rejected);

public sealed record ReviewSessionCard(
    Guid CardId,
    Guid? TermId,
    LearningCardMode Mode,
    string PromptLanguage,
    string AnswerLanguage,
    string Prompt,
    string? PromptReading,
    string? Answer,
    string? AnswerReading,
    int IntervalDays,
    IReadOnlyDictionary<ReviewRating, string> Intervals,
    ReviewAnimeContext? Context);
