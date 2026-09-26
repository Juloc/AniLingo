using System.Text.Encodings.Web;
using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.Api;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AniLingo.Tests;

/// <summary>
/// Exercises <see cref="AcquisitionApiKeyAuthenticationHandler"/> directly (no host/TestServer,
/// matching this suite's convention of testing services against a real isolated SQLite database)
/// to cover the acceptance criteria: a valid key authenticates as the owner, a missing header
/// yields no result (letting the cookie scheme try instead), and a revoked/invalid key produces
/// the 401 problem-details challenge.
/// </summary>
[TestClass]
public sealed class AcquisitionApiKeyAuthenticationHandlerTests
{
    [TestMethod]
    public async Task ValidKeyAuthenticatesAsOwnerScopedToThisScheme()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var keys = new AcquisitionApiKeyService(db);
            var (_, rawKey) = await keys.CreateAsync("Automation script", CancellationToken.None);

            var (handler, _) = await CreateInitializedHandlerAsync(keys, rawKey);
            var result = await handler.AuthenticateAsync();

            Assert.IsTrue(result.Succeeded, result.Failure?.Message);
            Assert.IsTrue(result.Principal!.IsInRole(AccountRoles.Owner));
            Assert.AreEqual(AcquisitionApiKeyAuthenticationHandler.SchemeName, result.Ticket!.AuthenticationScheme);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task MissingHeaderYieldsNoResultInsteadOfFailure()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var keys = new AcquisitionApiKeyService(db);

            var (handler, _) = await CreateInitializedHandlerAsync(keys, rawKey: null);
            var result = await handler.AuthenticateAsync();

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Failure, "A missing header is 'no result', not a failure, so the cookie scheme still gets a chance.");
            Assert.IsNull(result.Principal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    [DataRow("alk_totally-invalid-and-never-issued")]
    [DataRow("garbage")]
    public async Task InvalidOrUnknownKeyFailsAuthenticationAndChallengeReturns401ProblemDetails(string rawKey)
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var keys = new AcquisitionApiKeyService(db);

            var (handler, httpContext) = await CreateInitializedHandlerAsync(keys, rawKey);
            var result = await handler.AuthenticateAsync();
            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Failure);

            await handler.ChallengeAsync(null);
            var (status, problem) = ReadResponse(httpContext);
            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.AreEqual(StatusCodes.Status401Unauthorized, problem.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task RevokedKeyFailsAuthentication()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var keys = new AcquisitionApiKeyService(db);
            var (key, rawKey) = await keys.CreateAsync("Script", CancellationToken.None);
            await keys.RevokeAsync(key.Id, CancellationToken.None);

            var (handler, _) = await CreateInitializedHandlerAsync(keys, rawKey);
            var result = await handler.AuthenticateAsync();

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Failure);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ForbiddenWritesA403ProblemDetailsBody()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var db = new AppDbContext(CreateOptions(databasePath));
            await DatabaseMigrationBridge.UpgradeAsync(db);
            var (handler, httpContext) = await CreateInitializedHandlerAsync(
                new AcquisitionApiKeyService(db),
                rawKey: null);

            await handler.ForbidAsync(null);

            var (status, problem) = ReadResponse(httpContext);
            Assert.AreEqual(StatusCodes.Status403Forbidden, status);
            Assert.AreEqual(StatusCodes.Status403Forbidden, problem.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static async Task<(AcquisitionApiKeyAuthenticationHandler Handler, HttpContext HttpContext)> CreateInitializedHandlerAsync(
        AcquisitionApiKeyService keys,
        string? rawKey)
    {
        var handler = new AcquisitionApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            keys);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();
        if (rawKey is not null)
        {
            httpContext.Request.Headers[AcquisitionApiKeyAuthenticationHandler.HeaderName] = rawKey;
        }

        var scheme = new AuthenticationScheme(
            AcquisitionApiKeyAuthenticationHandler.SchemeName,
            AcquisitionApiKeyAuthenticationHandler.SchemeName,
            typeof(AcquisitionApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);
        return (handler, httpContext);
    }

    private static (int Status, ProblemDetails Problem) ReadResponse(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, leaveOpen: true);
        var json = reader.ReadToEnd();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(json)!;
        return (httpContext.Response.StatusCode, problem);
    }

    private static DbContextOptions<AppDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
            .Options;

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-acquisition-api-key-auth-{Guid.NewGuid():N}.db");

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
