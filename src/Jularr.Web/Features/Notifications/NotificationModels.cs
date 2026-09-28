using Jularr.Web.Features.Events;

namespace Jularr.Web.Features.Notifications;

/// <summary>
/// A profile's chosen delivery for one event category. Only <see cref="Off"/> and
/// <see cref="InApp"/> are wired to a real sink today (#429 v1); <see cref="Push"/> and
/// <see cref="Digest"/> exist so the model does not need another migration once a push/webhook
/// sink and digest batching land (tracked as a follow-up — see the PR description).
/// </summary>
public enum NotificationMode
{
    Off = 0,
    InApp = 1,
    Push = 2,
    Digest = 3
}

/// <summary>One profile's preference for one event category. Absence of a row means the default (in-app).</summary>
public sealed record NotificationSubscription(
    string ProfileId,
    JularrEventCategory Category,
    NotificationMode Mode,
    DateTime UpdatedAtUtc)
{
    public const NotificationMode DefaultMode = NotificationMode.InApp;
}

/// <summary>One inbox row: an event delivered to one profile through the in-app sink.</summary>
public sealed record NotificationItem(
    Guid Id,
    string ProfileId,
    Guid EventId,
    JularrEventCategory Category,
    JularrEventSeverity Severity,
    string? MediaType,
    string? SubjectId,
    IReadOnlyDictionary<string, string>? MessageParams,
    string? DeepLink,
    string? DedupKey,
    int OccurrenceCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ReadAtUtc)
{
    public bool IsRead => ReadAtUtc is not null;

    public string MessageKey => JularrEventCategories.Of(Category).MessageKey;
}

/// <summary>What the in-app sink (or a future sink) needs to record one delivery.</summary>
public sealed record NotificationDraft(
    string ProfileId,
    Guid EventId,
    JularrEventCategory Category,
    JularrEventSeverity Severity,
    string? MediaType,
    string? SubjectId,
    IReadOnlyDictionary<string, string>? MessageParams,
    string? DeepLink,
    string? DedupKey)
{
    public static NotificationDraft From(JularrEvent domainEvent, string profileId) =>
        new(
            profileId,
            domainEvent.Id,
            domainEvent.Category,
            domainEvent.Severity,
            domainEvent.MediaType,
            domainEvent.SubjectId,
            domainEvent.MessageParams,
            domainEvent.DeepLink,
            domainEvent.DedupKey);
}
