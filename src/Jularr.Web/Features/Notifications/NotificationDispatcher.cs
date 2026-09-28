using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Events;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Notifications;

/// <summary>
/// Turns one published <see cref="JularrEvent"/> into per-profile deliveries: resolves who should
/// see it, checks each recipient's <see cref="NotificationSubscriptionStore"/> preference, then
/// runs every registered <see cref="INotificationSink"/> for that preference. A sink failure is
/// logged and skipped, never rethrown, so one broken channel (for example a future webhook) can
/// never fail the acquisition/import/request flow that raised the event.
/// </summary>
public sealed class NotificationDispatcher(
    AppDbContext db,
    NotificationSubscriptionStore subscriptions,
    IEnumerable<INotificationSink> sinks,
    ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchAsync(JularrEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        foreach (var profileId in await ResolveRecipientsAsync(domainEvent, cancellationToken))
        {
            var mode = await subscriptions.GetModeAsync(profileId, domainEvent.Category, cancellationToken);
            if (mode == NotificationMode.Off)
            {
                continue;
            }

            foreach (var sink in sinks.Where(sink => sink.Mode == mode))
            {
                try
                {
                    await sink.DeliverAsync(domainEvent, profileId, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        exception,
                        "Notification sink {Sink} failed to deliver event {EventId} ({Category}) to profile {ProfileId}.",
                        sink.Key,
                        domainEvent.Id,
                        domainEvent.Category,
                        profileId);
                }
            }
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(JularrEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Audience == JularrEventAudience.Profile)
        {
            // No specific profile to notify (for example a system-triggered import): fall back to
            // admins rather than silently dropping the event.
            return string.IsNullOrWhiteSpace(domainEvent.ProfileId)
                ? await AdminProfileIdsAsync(cancellationToken)
                : [domainEvent.ProfileId];
        }

        return await AdminProfileIdsAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> AdminProfileIdsAsync(CancellationToken cancellationToken) =>
        await db.OwnerAccounts
            .AsNoTracking()
            .Where(account => account.IsEnabled && (account.Role == AccountRole.Owner || account.Role == AccountRole.MediaManager))
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
}
