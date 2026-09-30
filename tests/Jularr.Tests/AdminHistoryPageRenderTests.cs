using System.Globalization;
using System.Net;
using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Shell;
using Jularr.Web.Frontend;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jularr.Tests;

/// <summary>Admin → History rendered end to end through a real HTTP request.</summary>
[TestClass]
public sealed class AdminHistoryPageRenderTests
{
    private static readonly DateTime Stamp = new(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task HistoryShowsCategoryResultAndWhoPerformedEachEntryWithPillCounts()
    {
        await using var host = await HistoryHost.CreateAsync();
        host.Db.OwnerAccounts.Add(new OwnerAccount { Id = "alice-id", UserName = "alice", NormalizedUserName = "ALICE", PasswordHash = "x", Role = AccountRole.User });
        await host.Db.SaveChangesAsync();
        await host.FinishedAsync("Frieren import", "anime-import", "Library", OperationStatus.Succeeded, Stamp, "alice-id");
        await host.FinishedAsync("Nightly scan", "library-scan", "Library", OperationStatus.Failed, Stamp.AddHours(1));
        await host.FinishedAsync("Remux episode", "media-optimization", "Library", OperationStatus.Interrupted, Stamp.AddHours(2));

        var html = await host.GetHtmlAsync("/Admin/History");

        foreach (var pill in new[] { "All", "Acquisition", "Imports", "Remux", "Subtitle", "Translation", "Metadata", "AI", "Maintenance" })
        {
            StringAssert.Contains(html, pill);
        }

        StringAssert.Contains(html, "Frieren import");
        StringAssert.Contains(html, "admhist-tag-imports");
        StringAssert.Contains(html, "admhist-tag-remux");
        StringAssert.Contains(html, "admhist-result-success");
        StringAssert.Contains(html, "admhist-result-failed");
        StringAssert.Contains(html, "admhist-result-warning");
        StringAssert.Contains(html, "alice");
        StringAssert.Contains(html, "Manual");
        StringAssert.Contains(html, "System");
        StringAssert.Contains(html, "Automatic");
        StringAssert.Contains(html, "2026-09-10");
        StringAssert.Contains(html, "1–3 of 3");
        StringAssert.Contains(html, "Something broke.");
        StringAssert.Contains(html, "/Admin/Operation/");
        StringAssert.Contains(html, "returnUrl=%2FAdmin%2FHistory");
    }

    [TestMethod]
    public async Task CategoryResultDateAndSearchNarrowTheListAndKeepTheOthersCounted()
    {
        await using var host = await HistoryHost.CreateAsync();
        await host.FinishedAsync("Frieren import", "anime-import", "Library", OperationStatus.Succeeded, Stamp);
        await host.FinishedAsync("Dungeon import", "anime-import", "Library", OperationStatus.Failed, Stamp.AddDays(5));
        await host.FinishedAsync("Nightly scan", "library-scan", "Library", OperationStatus.Succeeded, Stamp);

        var imports = await host.GetHtmlAsync("/Admin/History?cat=imports");
        StringAssert.Contains(imports, "Frieren import");
        StringAssert.Contains(imports, "Dungeon import");
        Assert.IsFalse(imports.Contains("Nightly scan", StringComparison.Ordinal));

        var failed = await host.GetHtmlAsync("/Admin/History?result=failed");
        StringAssert.Contains(failed, "Dungeon import");
        Assert.IsFalse(failed.Contains("Frieren import", StringComparison.Ordinal));

        var range = await host.GetHtmlAsync("/Admin/History?from=2026-09-10&to=2026-09-10");
        StringAssert.Contains(range, "Frieren import");
        StringAssert.Contains(range, "Nightly scan");
        Assert.IsFalse(range.Contains("Dungeon import", StringComparison.Ordinal));

        var searched = await host.GetHtmlAsync("/Admin/History?q=dungeon");
        StringAssert.Contains(searched, "Dungeon import");
        Assert.IsFalse(searched.Contains("Nightly scan", StringComparison.Ordinal));

        var none = await host.GetHtmlAsync("/Admin/History?q=zzz");
        StringAssert.Contains(none, "No history entries match these filters.");
    }

    [TestMethod]
    public async Task AnEmptyHistoryAndAPagePastTheEndRenderCleanly()
    {
        await using var host = await HistoryHost.CreateAsync();

        StringAssert.Contains(await host.GetHtmlAsync("/Admin/History"), "Nothing has been recorded yet.");

        for (var index = 0; index < AdminHistoryQuery.PageSize + 1; index++)
        {
            await host.FinishedAsync($"Scan {index:00}", "library-scan", "Library", OperationStatus.Succeeded, Stamp.AddMinutes(index));
        }

        var last = await host.GetHtmlAsync("/Admin/History?p=99");
        StringAssert.Contains(last, "21–21 of 21");
        StringAssert.Contains(last, "Scan 00");
        StringAssert.Contains(last, "aria-current=\"page\"");
        StringAssert.Contains(last, "rel=\"prev\"");
        Assert.IsFalse(last.Contains("rel=\"next\"", StringComparison.Ordinal));
    }

    private sealed class HistoryHost : IAsyncDisposable
    {
        private readonly IHost host;
        private readonly TestServer server;

        private HistoryHost(AppDbContext db, IHost host, TestServer server)
        {
            Db = db;
            this.host = host;
            this.server = server;
        }

        public AppDbContext Db { get; }

        public static async Task<HistoryHost> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"jularr-history-page-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={path};Foreign Keys=True";

            var host = await new HostBuilder()
                .ConfigureWebHost(webBuilder => webBuilder
                    .UseTestServer()
                    .UseContentRoot(FindWebProjectRoot())
                    .ConfigureServices(services =>
                    {
                        services
                            .AddRazorPages()
                            .AddApplicationPart(typeof(Jularr.Web.Pages.Admin.HistoryModel).Assembly);
                        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
                        services.AddHttpContextAccessor();
                        services.AddAuthorization(options => JularrPolicies.Register(options));
                        services.AddAuthentication("test").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ForbiddenAnswerHandler>("test", _ => { });
                        services.AddLogging();
                        services.AddSingleton<ViteAssetManifest>();
                        services.AddScoped<CurrentAccountContext>();
                        services.AddSingleton(new MediaCapabilityStore(Path.Combine(Path.GetTempPath(), $"jularr-history-caps-{Guid.NewGuid():N}")));
                        services.AddScoped<IMediaCapabilityService, MediaCapabilityService>();
                        services.AddScoped<IAppShellService, AppShellService>();
                    })
                    .Configure(app =>
                    {
                        app.Use(async (context, next) =>
                        {
                            var claims = new List<Claim>
                            {
                                new(ClaimTypes.NameIdentifier, "test-owner"),
                                new(ClaimTypes.Role, AccountRoles.Owner)
                            };
                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
                            await next();
                        });
                        app.UseRouting();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                    }))
                .StartAsync();

            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new HistoryHost(db, host, host.GetTestServer());
        }

        public async Task<Guid> FinishedAsync(
            string title,
            string kind,
            string category,
            OperationStatus status,
            DateTime finishedAtUtc,
            string? actor = null)
        {
            var store = new OperationStore(Db);
            var id = await store.CreateAsync(new OperationDescriptor(kind, category, title, ActorProfileId: actor));
            switch (status)
            {
                case OperationStatus.Succeeded:
                    await store.MarkSucceededAsync(id);
                    break;
                case OperationStatus.Failed:
                    await store.MarkFailedAsync(id, "Something broke.");
                    break;
                default:
                    await store.MarkInterruptedAsync(id);
                    break;
            }

            var stamp = finishedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            await Db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Operations" SET "FinishedAtUtc" = {stamp}, "UpdatedAtUtc" = {stamp} WHERE "Id" = {id.ToString("D")}""");
            return id;
        }

        public async Task<string> GetHtmlAsync(string path)
        {
            using var client = server.CreateClient();
            using var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"GET {path} failed:\n{html}");

            // Razor encodes non-ASCII text as character references; assertions read the text a browser shows.
            return WebUtility.HtmlDecode(html);
        }

        public async ValueTask DisposeAsync()
        {
            server.Dispose();
            await host.StopAsync();
            host.Dispose();
            await Db.DisposeAsync();
        }

        private static string FindWebProjectRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
                {
                    return Path.Combine(directory.FullName, "src", "Jularr.Web");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
        }
    }

    private sealed class ForbiddenAnswerHandler(
        IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
    }
}
