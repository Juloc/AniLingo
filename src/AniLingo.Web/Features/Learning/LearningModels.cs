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
    public DateTimeOffset? NextReviewAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Review
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid TermId { get; set; }
    public ReviewRating Rating { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextReviewAt { get; set; }
}

public sealed record ReviewSchedule(DateTimeOffset NextReviewAt, int IntervalDays);

public interface IReviewScheduler
{
    ReviewSchedule Schedule(DateTimeOffset now, int currentIntervalDays, ReviewRating rating);
}

public sealed class BasicReviewScheduler : IReviewScheduler
{
    public ReviewSchedule Schedule(DateTimeOffset now, int currentIntervalDays, ReviewRating rating)
    {
        return rating switch
        {
            ReviewRating.Again => new ReviewSchedule(now.AddMinutes(10), 0),
            ReviewRating.Hard => new ReviewSchedule(now.AddDays(Math.Max(1, currentIntervalDays)), Math.Max(1, currentIntervalDays)),
            ReviewRating.Good => FromDays(now, currentIntervalDays == 0 ? 3 : Math.Max(2, (int)Math.Round(currentIntervalDays * 2.3))),
            ReviewRating.Easy => FromDays(now, currentIntervalDays == 0 ? 7 : Math.Max(4, (int)Math.Round(currentIntervalDays * 3.2))),
            _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, null)
        };
    }

    private static ReviewSchedule FromDays(DateTimeOffset now, int days) =>
        new(now.AddDays(days), days);
}

public sealed record DueReviewItem(Guid TermId, string Canonical, string? Reading, string? Meaning, int IntervalDays);
