using Jularr.Web.Features.Events;

namespace Jularr.Web.Features.Notifications;

/// <summary>
/// One notification destination. <see cref="InAppNotificationSink"/> is the only sink wired up in
/// v1; a webhook/Home Assistant, Web Push or e-mail sink (#429 follow-up) plugs in the same way —
/// register it in DI and <see cref="NotificationDispatcher"/> picks it up automatically. A sink
/// must never throw past <see cref="DeliverAsync"/>; the dispatcher already isolates failures per
/// sink, but a well-behaved sink should not fail the caller's media operation either.
/// </summary>
public interface INotificationSink
{
    /// <summary>Stable identifier, for logging and future per-destination settings.</summary>
    string Key { get; }

    /// <summary>The <see cref="NotificationMode"/> this sink acts on; other modes are ignored.</summary>
    NotificationMode Mode { get; }

    Task DeliverAsync(
        JularrEvent domainEvent,
        string profileId,
        CancellationToken cancellationToken);
}

/// <summary>The baseline destination (#429): a durable, profile-scoped inbox row.</summary>
public sealed class InAppNotificationSink(NotificationStore store) : INotificationSink
{
    public string Key => "in-app";

    public NotificationMode Mode => NotificationMode.InApp;

    public Task DeliverAsync(JularrEvent domainEvent, string profileId, CancellationToken cancellationToken) =>
        store.CreateAsync(NotificationDraft.From(domainEvent, profileId), cancellationToken);
}
