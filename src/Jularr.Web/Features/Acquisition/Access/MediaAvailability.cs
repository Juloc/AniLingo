namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>Where a title stands for the profile looking at it. A title that is neither in the library nor requested has no state.</summary>
public enum MediaAvailabilityState
{
    /// <summary>A request for the title is open: waiting for a decision, being searched or downloaded.</summary>
    Requested,

    /// <summary>The title is in the library but nothing of it is playable or readable yet.</summary>
    Local,

    /// <summary>Something of the title can be played or read now.</summary>
    Available
}

/// <summary>
/// The facts one card knows about a title. <see cref="Request"/> is the status of the open request for it,
/// if there is one (finished requests say nothing about the title's state and are not passed).
/// </summary>
public sealed record MediaAvailabilityFacts(
    bool InLibrary,
    bool HasPlayableContent,
    AcquisitionRequestStatus? Request = null);

public static class MediaAvailability
{
    /// <summary>
    /// Playable content wins, then an open request (the more useful thing to know while the title is on
    /// its way), then plain library presence. Returns null when there is nothing to say.
    /// </summary>
    public static MediaAvailabilityState? Resolve(MediaAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.HasPlayableContent)
        {
            return MediaAvailabilityState.Available;
        }

        if (facts.Request is { } status && AcquisitionAccessNames.IsOpen(status))
        {
            return MediaAvailabilityState.Requested;
        }

        return facts.InLibrary ? MediaAvailabilityState.Local : null;
    }
}
