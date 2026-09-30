using System.Reflection;
using Jularr.Web.Data;

namespace Jularr.Tests;

/// <summary>
/// Guards the real dependency-injection container (#649): <c>Program</c> is booted with the container-check
/// flag, which builds the service provider with <c>ValidateOnBuild</c> and <c>ValidateScopes</c> and exits
/// before touching the database. An unregistered constructor dependency (like the missing
/// <c>MediaFactsService</c> registration that broke the Collections pages) or a scoped service captured by
/// a singleton fails here instead of at runtime.
/// </summary>
[TestClass]
public sealed class ServiceContainerValidationTests
{
    [TestMethod]
    public async Task EveryRegisteredServiceCanBeConstructedFromTheRealContainer()
    {
        var entryPoint = typeof(AppDbContext).Assembly.EntryPoint
            ?? throw new AssertFailedException("Jularr.Web has no entry point.");
        var keysDirectory = Path.Combine(Path.GetTempPath(), "jularr-container-check-" + Guid.NewGuid().ToString("N"));

        try
        {
            string[] args =
            [
                "--Jularr:ContainerCheckOnly=true",
                "--ConnectionStrings:Default=Host=localhost;Database=container-check",
                $"--DataProtection:KeysDirectory={keysDirectory}",
                "--urls=http://127.0.0.1:0"
            ];

            var result = entryPoint.Invoke(null, [args]);
            if (result is Task task)
            {
                await task;
            }
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            var cause = ex is TargetInvocationException { InnerException: not null } wrapped ? wrapped.InnerException! : ex;
            Assert.Fail("The DI container failed validation: " + cause.Message);
        }
        finally
        {
            if (Directory.Exists(keysDirectory))
            {
                Directory.Delete(keysDirectory, recursive: true);
            }
        }
    }
}
