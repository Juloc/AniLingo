using Jularr.Web.Features.Events;

namespace Jularr.Tests;

/// <summary>
/// A test double for <see cref="IJularrEventPublisher"/>: records every published event instead of
/// touching a database, so tests of code that now depends on the #429 event boundary (for example
/// <c>AcquisitionRequestService</c>) do not need a full notification pipeline wired up just to
/// compile and run.
/// </summary>
public sealed class RecordingEventPublisher : IJularrEventPublisher
{
    public List<JularrEvent> Published { get; } = [];

    public Task PublishAsync(JularrEvent domainEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(domainEvent);
        return Task.CompletedTask;
    }
}
