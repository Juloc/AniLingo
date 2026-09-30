using System.Collections.Concurrent;
using Jularr.Web.Data;
using Jularr.Web.Features.Operations;
using Jularr.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>The worker of the background queue takes the highest priority first and the oldest first among equals.</summary>
[TestClass]
public sealed class BackgroundJobPriorityTests
{
    [TestMethod]
    public async Task QueuedWorkRunsByPriorityThenByAgeAndAChangeWhileWaitingCounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jularr-job-priority-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={path};Foreign Keys=True"));
        services.AddSingleton<BackgroundJobQueue>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await DatabaseMigrationBridge.UpgradeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }

        var queue = provider.GetRequiredService<BackgroundJobQueue>();
        var ran = new ConcurrentQueue<string>();

        async Task<Guid> QueueAsync(string name, OperationPriority priority) =>
            await queue.QueueAsync(
                new OperationDescriptor("test-job", "Task", name, Priority: priority),
                (_, _) =>
                {
                    ran.Enqueue(name);
                    return Task.CompletedTask;
                });

        // Everything waits before the worker starts, so the order is the worker's choice alone.
        await QueueAsync("first-low", OperationPriority.Low);
        await QueueAsync("second-normal", OperationPriority.Normal);
        var raised = await QueueAsync("third-raised", OperationPriority.Normal);
        await QueueAsync("fourth-normal", OperationPriority.Normal);
        await QueueAsync("fifth-high", OperationPriority.High);

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = new OperationStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            Assert.IsTrue(await store.SetPriorityAsync(raised, OperationPriority.High));
        }

        using var worker = new BackgroundJobWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BackgroundJobWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (ran.Count < 5 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        CollectionAssert.AreEqual(
            new[] { "third-raised", "fifth-high", "second-normal", "fourth-normal", "first-low" },
            ran.ToArray());
    }
}
