using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.History;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Ownership;
using Jularr.Web.Features.Acquisition.Pipeline;
using Jularr.Web.Features.Acquisition.Policy;
using Jularr.Web.Features.Acquisition.Quality;
using Jularr.Web.Features.Acquisition.Sabnzbd;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Calendar;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Progress;
using Jularr.Web.Features.Sonarr;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Pages.Library;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// Renders real Jularr.Web Razor Pages end to end (real routing, tag helpers, the compiled
/// _ManageSheet partial) through a real HTTP request, so tests can assert on the exact HTML a
/// browser would receive for an owner vs. a normal user (#519, part of epic #510). Everything
/// registered here is the same concrete production service Program.cs wires for these pages,
/// minus the pieces that need a real network/root filesystem (AniList's HttpClient throws if a
/// test path ever calls out; a GET never should).
/// </summary>
internal sealed class ManageSheetPageTestHost : IAsyncDisposable
{
    public const string OwnerRoleHeader = "X-Test-Owner";

    private readonly string root;
    private readonly IHost host;
    private readonly TestServer server;

    private ManageSheetPageTestHost(string root, AppDbContext db, IHost host, TestServer server)
    {
        this.root = root;
        Db = db;
        this.host = host;
        this.server = server;
    }

    public AppDbContext Db { get; }

    public static async Task<ManageSheetPageTestHost> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"jularr-manage-sheet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dataDirectory = new DirectoryInfo(Path.Combine(root, "data"));
        dataDirectory.Create();
        var databasePath = Path.Combine(root, "jularr.db");
        var connectionString = $"Data Source={databasePath};Foreign Keys=True";

        var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseContentRoot(FindWebProjectRoot())
                    .ConfigureServices(services =>
                    {
                        services
                            .AddRazorPages()
                            .AddApplicationPart(typeof(AnimeModel).Assembly);

                        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
                        services.AddHttpContextAccessor();
                        services.AddScoped<CurrentAccountContext>();
                        services.AddScoped<OperationRunner>();
                        services.AddScoped<EpisodeProgressService>();
                        services.AddScoped<FranchiseStore>();
                        // The "Upcoming releases" view component every consumer detail page
                        // includes; no IReleaseEventSource is registered, so it just renders empty.
                        services.AddScoped<ReleaseCalendarService>();
                        services.AddSingleton(TimeProvider.System);
                        services.AddScoped<AnimeMetadataService>();
                        services.AddScoped<AniListAccountService>();

                        var protectionProvider = new EphemeralDataProtectionProvider();
                        services.AddSingleton(new AniListAccountStore(
                            protectionProvider,
                            NullLogger<AniListAccountStore>.Instance,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "anilist"))));
                        services.AddSingleton(new MediaMappingReviewStore(
                            NullLogger<MediaMappingReviewStore>.Instance,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "mapping-review"))));
                        services.AddSingleton(new ReadingSegmentMappingStore(
                            NullLogger<ReadingSegmentMappingStore>.Instance,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "reading-segments"))));

                        // AnimeModel's GET never reaches the network (progress summary is
                        // local-only; see the comment on ExternalProgress in Anime.cshtml.cs), so
                        // a handler that fails loudly on any actual request is safer than
                        // silently hanging.
                        services.AddSingleton(new HttpClient(new NeverCallHandler())
                        {
                            BaseAddress = new Uri("https://graphql.anilist.co/")
                        });

                        // The owner-only _AnimeAcquisitionPanel (part of the Manage sheet's
                        // Acquisition group) reads AnimeAcquisitionPipeline.GetAnimePanelAsync,
                        // a read-only status view. Nothing is configured (no indexers/download
                        // clients/Sonarr connection), so every store below is real but empty, and
                        // logging is a no-op sink (no provider attached).
                        services.AddLogging();
                        services.AddSingleton<IReadOnlyDictionary<IndexerType, IIndexer>>(
                            new Dictionary<IndexerType, IIndexer>());
                        services.AddSingleton(new IndexerStore(
                            protectionProvider,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "indexers"))));
                        services.AddSingleton(new AcquisitionHealthStore(
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "acquisition-health"))));
                        services.AddSingleton(new SonarrConnectionStore(
                            protectionProvider,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "sonarr"))));
                        services.AddSingleton(new AnimeImportSettingsStore(
                            Path.Combine(dataDirectory.FullName, "import-settings")));
                        services.AddSingleton(new DownloadClientStore(
                            protectionProvider,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "download-clients"))));
                        services.AddSingleton(new AnimeMonitoringStore(
                            Path.Combine(dataDirectory.FullName, "monitoring")));
                        services.AddSingleton(new AcquisitionOwnershipStore(
                            Path.Combine(dataDirectory.FullName, "ownership")));
                        services.AddSingleton(new AcquisitionPolicyStore(
                            Path.Combine(dataDirectory.FullName, "acquisition-policy")));
                        services.AddSingleton(new AnimeQualityProfileStore(
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "quality-profiles"))));
                        services.AddSingleton(new AnimeImportStore(
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "anime-imports"))));
                        services.AddSingleton(new SabnzbdAcquisitionStore(
                            protectionProvider,
                            new DirectoryInfo(Path.Combine(dataDirectory.FullName, "sabnzbd-acquisitions"))));
                        services.AddScoped<ISonarrObserverClient, SonarrObserverClient>();
                        services.AddScoped<ISabnzbdClient>(_ => new SabnzbdClient(
                            new HttpClient(new NeverCallHandler())));
                        services.AddScoped<IDownloadClient, SabnzbdDownloadClient>();
                        services.AddScoped<DownloadClientSelector>();
                        services.AddScoped<DownloadClientSubmissionService>();
                        services.AddScoped<SabnzbdDownloadService>();
                        services.AddScoped<SabnzbdAcquisitionService>();
                        services.AddScoped<SonarrObservationService>();
                        services.AddScoped<IndexerSearchCoordinator>();
                        services.AddScoped<AnimeAcquisitionInventory>();
                        services.AddScoped<AcquisitionHistoryService>();
                        services.AddScoped<AnimeAcquisitionPipeline>();
                        services.AddHttpClient();
                    })
                    .Configure(app =>
                    {
                        // Stand-in for real cookie authentication: the test picks owner vs.
                        // normal user with a header instead of signing in, everything downstream
                        // (CurrentAccountContext, the page views' Model.IsOwner) behaves exactly
                        // like a real signed-in request.
                        app.Use(async (context, next) =>
                        {
                            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "test-profile") };
                            if (context.Request.Headers.ContainsKey(OwnerRoleHeader))
                            {
                                claims.Add(new Claim(ClaimTypes.Role, AccountRoles.Owner));
                            }

                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
                            await next();
                        });
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                    });
            })
            .StartAsync();

        var server = host.GetTestServer();

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options);
        await DatabaseMigrationBridge.UpgradeAsync(db);

        return new ManageSheetPageTestHost(root, db, host, server);
    }

    /// <param name="asOwner">Adds the owner role claim to the simulated signed-in account.</param>
    public async Task<string> GetHtmlAsync(string path, bool asOwner)
    {
        using var client = server.CreateClient();
        if (asOwner)
        {
            client.DefaultRequestHeaders.Add(OwnerRoleHeader, "true");
        }

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(
            System.Net.HttpStatusCode.OK,
            response.StatusCode,
            $"GET {path} (owner={asOwner}) failed:\n{html}");
        return html;
    }

    public async Task<Anime> AddAnimeAsync(string title)
    {
        var anime = new Anime { Key = Guid.NewGuid().ToString("N"), Title = title };
        Db.Add(anime);
        await Db.SaveChangesAsync();
        return anime;
    }

    public async Task AddAnimeMetadataMatchAsync(Anime anime)
    {
        Db.Add(new AnimeMetadata
        {
            AnimeId = anime.Id,
            Provider = "anilist",
            ExternalId = "1",
            PreferredTitle = anime.Title
        });
        await Db.SaveChangesAsync();
    }

    public async Task<Episode> AddEpisodeAsync(Anime anime, int season, int number)
    {
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = season,
            Number = number,
            Title = $"Episode {number}"
        };
        Db.Add(episode);
        await Db.SaveChangesAsync();
        return episode;
    }

    public async ValueTask DisposeAsync()
    {
        server.Dispose();
        await host.StopAsync();
        host.Dispose();
        await Db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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

    private sealed class NeverCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected outbound request to {request.RequestUri} during a manage-sheet render test.");
    }
}
