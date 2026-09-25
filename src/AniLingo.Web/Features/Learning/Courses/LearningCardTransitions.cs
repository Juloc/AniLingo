namespace AniLingo.Web.Features.Learning.Courses;

/// <summary>
/// State transitions shared by every writer of <see cref="LearningCard"/>.
/// Scheduling fields (interval, due time) are otherwise owned by FSRS review
/// replay in <see cref="LearningService"/>.
/// </summary>
internal static class LearningCardTransitions
{
    public static void Apply(
        LearningCard card,
        UserTermState state,
        DateTime nowUtc,
        ref long queuePosition)
    {
        card.State = state;
        card.UpdatedAt = nowUtc;

        switch (state)
        {
            case UserTermState.Known:
            case UserTermState.Suspended:
                card.NextReviewAt = null;
                return;

            case UserTermState.Saved:
            case UserTermState.Ignored:
                card.NextReviewAt = null;
                card.LearningStartedAt = null;
                card.QueuePosition = null;
                return;

            case UserTermState.Learning:
                if (card.LearningStartedAt is null)
                {
                    card.NextReviewAt = null;
                    card.QueuePosition ??= ++queuePosition;
                }
                else
                {
                    card.NextReviewAt ??= nowUtc;
                }

                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    /// <summary>
    /// Puts a card that is not already Known or Learning at the end of the
    /// new-card queue. Returns false when the card was left unchanged.
    /// </summary>
    public static bool Queue(
        LearningCard card,
        DateTime nowUtc,
        ref long queuePosition)
    {
        if (card.State is UserTermState.Known or UserTermState.Learning)
        {
            return false;
        }

        card.State = UserTermState.Learning;
        card.NextReviewAt = null;
        card.LearningStartedAt = null;
        card.QueuePosition = ++queuePosition;
        card.UpdatedAt = nowUtc;
        return true;
    }
}
