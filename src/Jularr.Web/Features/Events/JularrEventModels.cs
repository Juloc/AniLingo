namespace Jularr.Web.Features.Events;

/// <summary>
/// Every meaningful domain event Jularr can raise (#429). Features publish one of these through
/// <see cref="IJularrEventPublisher"/> instead of calling a notification channel directly, so
/// delivery, subscriptions and deduplication live in one place.
/// </summary>
public enum JularrEventCategory
{
    DownloadGrabbed = 1,
    DownloadFailed = 2,
    ImportCompleted = 3,
    ImportFailed = 4,
    ReleaseAvailable = 5,
    RequestApproved = 6,
    RequestDenied = 7,
    StorageProblem = 8
}

public enum JularrEventSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>
/// Who an event is for. <see cref="Profile"/> events belong to the one profile named on the
/// event; <see cref="Admin"/> events (infrastructure/system problems) go to every owner/media
/// manager account instead, per the issue's "owner/admin errors are not exposed to normal
/// profiles" rule.
/// </summary>
public enum JularrEventAudience
{
    Profile = 1,
    Admin = 2
}

/// <summary>
/// Static metadata for each category: its default audience/severity and the translation key that
/// renders it. Kept separate from the event instance so call sites cannot disagree on routing.
/// </summary>
public static class JularrEventCategories
{
    public sealed record Meta(JularrEventAudience Audience, JularrEventSeverity Severity, string MessageKey, string LabelKey);

    public static readonly IReadOnlyDictionary<JularrEventCategory, Meta> All = new Dictionary<JularrEventCategory, Meta>
    {
        [JularrEventCategory.DownloadGrabbed] = new(JularrEventAudience.Profile, JularrEventSeverity.Info, "notifications.event.downloadGrabbed", "notifications.category.downloadGrabbed"),
        [JularrEventCategory.DownloadFailed] = new(JularrEventAudience.Profile, JularrEventSeverity.Warning, "notifications.event.downloadFailed", "notifications.category.downloadFailed"),
        [JularrEventCategory.ImportCompleted] = new(JularrEventAudience.Profile, JularrEventSeverity.Info, "notifications.event.importCompleted", "notifications.category.importCompleted"),
        [JularrEventCategory.ImportFailed] = new(JularrEventAudience.Profile, JularrEventSeverity.Warning, "notifications.event.importFailed", "notifications.category.importFailed"),
        [JularrEventCategory.ReleaseAvailable] = new(JularrEventAudience.Profile, JularrEventSeverity.Info, "notifications.event.releaseAvailable", "notifications.category.releaseAvailable"),
        [JularrEventCategory.RequestApproved] = new(JularrEventAudience.Profile, JularrEventSeverity.Info, "notifications.event.requestApproved", "notifications.category.requestApproved"),
        [JularrEventCategory.RequestDenied] = new(JularrEventAudience.Profile, JularrEventSeverity.Info, "notifications.event.requestDenied", "notifications.category.requestDenied"),
        [JularrEventCategory.StorageProblem] = new(JularrEventAudience.Admin, JularrEventSeverity.Critical, "notifications.event.storageProblem", "notifications.category.storageProblem")
    };

    public static Meta Of(JularrEventCategory category) => All[category];
}

/// <summary>
/// One canonical domain event (#429). <see cref="MessageParams"/> carries placeholder values for
/// <see cref="JularrEventCategories.Meta.MessageKey"/> (for example the media title) rather than
/// pre-rendered HTML, so every profile reads it in their own language.
/// </summary>
public sealed record JularrEvent(
    Guid Id,
    JularrEventCategory Category,
    JularrEventAudience Audience,
    string? ProfileId,
    string? MediaType,
    string? SubjectId,
    IReadOnlyDictionary<string, string>? MessageParams,
    JularrEventSeverity Severity,
    string? DeepLink,
    string? DedupKey,
    Guid? RelatedOperationId,
    DateTime CreatedAtUtc)
{
    /// <summary>
    /// Builds an event from its category's static metadata, so callers only supply what is
    /// specific to the occurrence.
    /// </summary>
    public static JularrEvent Create(
        JularrEventCategory category,
        string? profileId = null,
        string? mediaType = null,
        string? subjectId = null,
        IReadOnlyDictionary<string, string>? messageParams = null,
        string? deepLink = null,
        string? dedupKey = null,
        Guid? relatedOperationId = null)
    {
        var meta = JularrEventCategories.Of(category);
        return new JularrEvent(
            Guid.NewGuid(),
            category,
            meta.Audience,
            profileId,
            mediaType,
            subjectId,
            messageParams,
            meta.Severity,
            deepLink,
            dedupKey,
            relatedOperationId,
            DateTime.UtcNow);
    }
}
