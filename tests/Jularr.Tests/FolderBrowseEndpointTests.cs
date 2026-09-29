using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Storage.FolderBrowse;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jularr.Tests;

/// <summary>
/// The HTTP surface of the folder browser (#604): Owner only, JSON with machine codes, and a
/// request token before a folder is created.
/// </summary>
[TestClass]
public sealed class FolderBrowseEndpointTests
{
    private const string Roots = FolderBrowseEndpoints.BasePath + "/roots";
    private const string List = FolderBrowseEndpoints.BasePath + "/list";
    private const string Check = FolderBrowseEndpoints.BasePath + "/check";
    private const string Create = FolderBrowseEndpoints.BasePath + "/create";

    [TestMethod]
    public async Task OnlyTheOwnerMayBrowseOrCreateFolders()
    {
        await using var host = await EndpointHost.CreateAsync();

        foreach (var path in new[] { Roots, $"{List}?path={Uri.EscapeDataString(host.Media)}", $"{Check}?path={Uri.EscapeDataString(host.Media)}" })
        {
            using var anonymous = host.Client();
            using var manager = host.Client(EndpointHost.MediaManager);
            using var user = host.Client(EndpointHost.User);
            using var owner = host.Client(EndpointHost.Owner);

            Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode, path);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await manager.GetAsync(path)).StatusCode, path);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await user.GetAsync(path)).StatusCode, path);
            Assert.AreEqual(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode, path);
        }

        using var token = await host.TokenAsync(EndpointHost.MediaManager);
        var denied = await token.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media, "planted"));
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.IsFalse(Directory.Exists(Path.Combine(host.Media, "planted")));
    }

    [TestMethod]
    public async Task ListingsAreCamelCaseJsonAndRefusalsAreCodes()
    {
        await using var host = await EndpointHost.CreateAsync();
        Directory.CreateDirectory(Path.Combine(host.Media, "manga"));
        File.WriteAllText(Path.Combine(host.Media, "secret.txt"), "not listed");
        using var owner = host.Client(EndpointHost.Owner);

        var ok = await owner.GetAsync($"{List}?path={Uri.EscapeDataString(host.Media)}");
        var listing = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        Assert.AreEqual("no-store", ok.Headers.CacheControl?.ToString());
        Assert.AreEqual(host.Media, listing.GetProperty("path").GetString());
        Assert.AreEqual(1, listing.GetProperty("folders").GetArrayLength());
        Assert.AreEqual("manga", listing.GetProperty("folders")[0].GetProperty("name").GetString());
        Assert.IsTrue(listing.GetProperty("access").GetProperty("writable").GetBoolean());
        Assert.IsFalse(listing.ToString().Contains("secret.txt", StringComparison.Ordinal), "no file is ever listed");

        await AssertProblemAsync(owner, $"{List}?path={Uri.EscapeDataString(host.Media + "/../elsewhere")}", HttpStatusCode.BadRequest, "traversal");
        await AssertProblemAsync(owner, $"{List}?path=relative", HttpStatusCode.BadRequest, "notAbsolute");
        await AssertProblemAsync(owner, $"{List}?path={Uri.EscapeDataString(host.Outside)}", HttpStatusCode.Forbidden, "outsideStorage");
        await AssertProblemAsync(owner, $"{List}?path={Uri.EscapeDataString(Path.Combine(host.Media, "none"))}", HttpStatusCode.NotFound, "notFound");

        var roots = JsonDocument.Parse(await owner.GetStringAsync(Roots)).RootElement.GetProperty("roots");
        var media = roots.EnumerateArray().Single(root => root.GetProperty("path").GetString() == host.Media);
        Assert.AreEqual(2, roots.GetArrayLength(), "the mount and the data folder");
        Assert.IsTrue(media.GetProperty("access").GetProperty("readable").GetBoolean());
        Assert.AreEqual("ext4", media.GetProperty("fileSystem").GetString());
    }

    [TestMethod]
    public async Task CheckAnswersWithTheProblemAsAWordAndNeverWithAnError()
    {
        await using var host = await EndpointHost.CreateAsync();
        using var owner = host.Client(EndpointHost.Owner);

        var check = JsonDocument.Parse(await owner.GetStringAsync(
            $"{Check}?path={Uri.EscapeDataString(host.Outside)}")).RootElement;

        Assert.AreEqual("outsideStorage", check.GetProperty("problem").GetString());
        Assert.AreEqual("missing", check.GetProperty("kind").GetString());
    }

    [TestMethod]
    public async Task CreatingAFolderNeedsTheRequestTokenAndRefusesUnsafeInput()
    {
        await using var host = await EndpointHost.CreateAsync();
        using var withoutToken = host.Client(EndpointHost.Owner);

        var rejected = await withoutToken.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media, "planted"));
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.IsFalse(Directory.Exists(Path.Combine(host.Media, "planted")));

        using var session = await host.TokenAsync(EndpointHost.Owner);
        var created = await session.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media, "planted"));
        var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.AreEqual("created", body.GetProperty("outcome").GetString());
        Assert.AreEqual(Path.Combine(host.Media, "planted"), body.GetProperty("path").GetString());
        Assert.IsTrue(Directory.Exists(Path.Combine(host.Media, "planted")));

        var again = await session.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media, "planted"));
        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);

        var badName = await session.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media, "../planted"));
        var badNameBody = JsonDocument.Parse(await badName.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.AreEqual("invalidName", badNameBody.GetProperty("outcome").GetString());
        Assert.AreEqual("invalidCharacter", badNameBody.GetProperty("nameProblem").GetString());

        var traversal = await session.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Media + "/../outside", "planted"));
        var traversalBody = JsonDocument.Parse(await traversal.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(HttpStatusCode.BadRequest, traversal.StatusCode);
        Assert.AreEqual("invalidParent", traversalBody.GetProperty("outcome").GetString());
        Assert.AreEqual("traversal", traversalBody.GetProperty("parentProblem").GetString());
        Assert.IsFalse(Directory.Exists(Path.Combine(host.Outside, "planted")));

        var outside = await session.Client.PostAsJsonAsync(Create, new CreateFolderRequest(host.Outside, "planted"));
        Assert.AreEqual(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.IsFalse(Directory.Exists(Path.Combine(host.Outside, "planted")));
    }

    private static async Task AssertProblemAsync(HttpClient client, string url, HttpStatusCode status, string problem)
    {
        var response = await client.GetAsync(url);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.AreEqual(status, response.StatusCode, url);
        Assert.AreEqual(problem, body.GetProperty("problem").GetString(), url);
    }

    private sealed class EndpointHost : IAsyncDisposable
    {
        public const string Owner = "Owner";
        public const string MediaManager = "MediaManager";
        public const string User = "User";
        private const string RoleHeader = "X-Test-Role";

        private readonly string root;
        private readonly IHost host;
        private readonly TestServer server;

        private EndpointHost(string root, IHost host, TestServer server)
        {
            this.root = root;
            this.host = host;
            this.server = server;
            Media = Path.Combine(root, "media");
            Outside = Path.Combine(root, "outside");
        }

        public string Media { get; }

        public string Outside { get; }

        public static async Task<EndpointHost> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"jularr-folder-endpoints-{Guid.NewGuid():N}");
            var media = Directory.CreateDirectory(Path.Combine(root, "media")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "outside"));
            var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDataProtection().UseEphemeralDataProtectionProvider();
                        services.AddAntiforgery();
                        services.AddAuthorization(options => JularrPolicies.Register(options));
                        services.AddAuthentication("test").AddCookie("test", options =>
                        {
                            options.Events.OnRedirectToLogin = context =>
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                return Task.CompletedTask;
                            };
                            options.Events.OnRedirectToAccessDenied = context =>
                            {
                                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                                return Task.CompletedTask;
                            };
                        });
                        services.AddSingleton<IMountTable>(new FolderBrowseTests.FakeMountTable(
                            [new MountPoint(media, "ext4", null, ReadOnly: false)]));
                        services.AddSingleton<IDirectoryAccess, DirectoryAccess>();
                        services.AddSingleton(new FolderBrowseOptions(data));
                        services.AddSingleton<FolderBrowseService>();
                    })
                    .Configure(app =>
                    {
                        // The role comes from a header instead of a cookie sign-in.
                        app.Use(async (context, next) =>
                        {
                            if (context.Request.Headers.TryGetValue(RoleHeader, out var role))
                            {
                                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                                    [new Claim(ClaimTypes.NameIdentifier, "test"), new Claim(ClaimTypes.Role, role.ToString())],
                                    "test"));
                            }

                            await next();
                        });
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapFolderBrowse();
                            endpoints.MapGet("/token", (IAntiforgery antiforgery, HttpContext context) =>
                                Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
                        });
                    }))
                .StartAsync();

            return new EndpointHost(root, host, host.GetTestServer());
        }

        public HttpClient Client(string? role = null)
        {
            var client = server.CreateClient();
            if (role is not null)
            {
                client.DefaultRequestHeaders.Add(RoleHeader, role);
            }

            return client;
        }

        /// <summary>A client that carries the antiforgery cookie and request token a page would have.</summary>
        public async Task<TokenSession> TokenAsync(string role)
        {
            var client = Client(role);
            var response = await client.GetAsync("/token");
            var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
            var cookies = response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]);
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
            client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
            return new TokenSession(client);
        }

        public async ValueTask DisposeAsync()
        {
            server.Dispose();
            await host.StopAsync();
            host.Dispose();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class TokenSession(HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose() => Client.Dispose();
    }
}
