using System.Net;
using System.Security.Claims;
using System.Text;
using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using AiSettingsModel = Jularr.Web.Pages.Settings.AiModel;

namespace Jularr.Tests;

/// <summary>
/// Settings → AI end to end (#422): first-load model discovery, the explicit model picker (never an
/// implicit provider default), provider switches and the "Recommended for books" preset.
/// </summary>
[TestClass]
public sealed class AiControlCenterPageTests
{
    private const string Profile = "owner";

    [TestMethod]
    public async Task FirstVisitDiscoversModelsOnceAfterTheLocalRenderAndThenServesTheCache()
    {
        await using var fixture = await PageFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? Json("""{"data":[{"id":"model-a"},{"id":"model-b"}]}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        await fixture.Settings.SaveAsync(Profile, Personal("model-a"), CancellationToken.None);

        var first = fixture.Page();
        await first.OnGetAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.Requests, "The GET itself renders from local state.");
        Assert.IsTrue(first.DiscoverOnLoad, "An empty, never-asked catalog is discovered after the page rendered.");

        var discovered = (JsonResult)await fixture.Page().OnPostDiscoverModelsAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Requests);
        StringAssert.Contains(System.Text.Json.JsonSerializer.Serialize(discovered.Value), "\"models\":2");

        var second = fixture.Page();
        await second.OnGetAsync(CancellationToken.None);
        Assert.IsFalse(second.DiscoverOnLoad);
        CollectionAssert.AreEqual(new[] { "model-a", "model-b" }, second.PersonalCatalog.Models.Select(x => x.Id).ToArray());

        await fixture.Page().OnPostDiscoverModelsAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Requests, "Later loads stay cache-first; only Refresh models asks again.");

        // Refresh posts the form like the browser does (the loaded values, API key left empty).
        var refresh = fixture.Page();
        await refresh.OnGetAsync(CancellationToken.None);
        await refresh.OnPostRefreshModelsAsync(CancellationToken.None);
        Assert.AreEqual(2, fixture.Requests);
        Assert.AreEqual("test-key", (await fixture.Settings.LoadAsync(Profile, CancellationToken.None)).ApiKey, "An empty key field keeps the stored key.");
    }

    [TestMethod]
    public async Task PersonalProviderCanLoadModelsBeforeAModelIsChosen()
    {
        await using var fixture = await PageFixture.CreateAsync(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? Json("""{"data":[{"id":"model-a"}]}""")
                : Json("""{"choices":[{"message":{"content":"Hallo"}}]}"""));

        var page = fixture.Page();
        await page.OnGetAsync(CancellationToken.None);
        page.ProviderId = AiProviderIds.OpenAiCompatible;
        page.BaseUrl = "https://api.example.invalid/v1";
        page.ApiKey = "test-key";
        page.ModelName = null;
        Assert.IsInstanceOfType<RedirectToPageResult>(await page.OnPostRefreshModelsAsync(CancellationToken.None));
        Assert.AreEqual(1, fixture.Requests, "Base URL and key are enough to list the models.");

        var loaded = fixture.Page();
        await loaded.OnGetAsync(CancellationToken.None);
        Assert.AreEqual("model-a", loaded.PersonalCatalog.Models.Single().Id);
        Assert.IsTrue(loaded.PersonalModelMissing);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Router().TranslateAsync("こんにちは", "de", CancellationToken.None));
        Assert.AreEqual(1, fixture.Requests, "No request without a model.");

        loaded.ModelName = AiSettingsModel.CustomModelOption;
        loaded.CustomModelName = "unlisted-model";
        await loaded.OnPostSaveAsync(CancellationToken.None);
        Assert.AreEqual("unlisted-model", (await fixture.Settings.LoadAsync(Profile, CancellationToken.None)).Model, "A model the provider does not list can still be entered.");

        var picked = fixture.Page();
        await picked.OnGetAsync(CancellationToken.None);
        picked.ModelName = "model-a";
        await picked.OnPostSaveAsync(CancellationToken.None);
        Assert.AreEqual("model-a", (await fixture.Settings.LoadAsync(Profile, CancellationToken.None)).Model);
    }

    [TestMethod]
    public async Task UnsupportedDiscoveryKeepsManualEntryWithAClearWarning()
    {
        await using var fixture = await PageFixture.CreateAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        // The server connection is unavailable here (no Codex binary/app-server in tests).
        await fixture.Page().OnPostDiscoverModelsAsync(CancellationToken.None);
        var page = fixture.Page();
        await page.OnGetAsync(CancellationToken.None);

        Assert.IsFalse(page.DiscoverOnLoad, "A failed first discovery is not retried on every visit.");
        Assert.IsFalse(page.ServerCatalog.HasModels);
        Assert.IsTrue(page.ServerModelMissing, "Without models or a manual model the page warns that AI tasks wait.");
        Assert.IsNull(page.BookPreset, "No preset is offered without a concrete model.");

        page.ProviderId = AiProviderIds.Server;
        page.ServerModel = "manual-model";
        await page.OnPostSaveAsync(CancellationToken.None);
        var saved = await fixture.Settings.LoadAsync(Profile, CancellationToken.None);
        Assert.AreEqual("manual-model", saved.Model, "Manual model entry works when discovery is unavailable.");

        var options = AiOptionResolver.ResolveServer(saved.Resolve(AiOperations.BookTranslation), page.ServerCatalog, AiOperations.BookTranslation);
        Assert.AreEqual("manual-model", options.Model);
    }

    [TestMethod]
    public async Task ThePickerShowsTheConcreteModelThatRunsNeverAProviderDefault()
    {
        await using var fixture = await PageFixture.CreateAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await fixture.SeedServerCatalogAsync();

        var fresh = fixture.Page();
        await fresh.OnGetAsync(CancellationToken.None);
        Assert.AreEqual("gpt-b", fresh.ServerModel, "Unset settings show the catalog's default model, selected explicitly.");
        Assert.IsNull(fresh.UnavailableServerModel);

        await fixture.Settings.SaveAsync(Profile, AiProfileSettings.Default with { Model = "retired" }, CancellationToken.None);
        var retired = fixture.Page();
        await retired.OnGetAsync(CancellationToken.None);
        Assert.AreEqual("gpt-b", retired.ServerModel, "A model the catalog no longer lists falls back to the known default.");
        Assert.AreEqual("retired", retired.UnavailableServerModel);

        retired.ProviderId = AiProviderIds.Server;
        await retired.OnPostSaveAsync(CancellationToken.None);
        Assert.AreEqual("gpt-b", (await fixture.Settings.LoadAsync(Profile, CancellationToken.None)).Model, "Saving persists the concrete model id.");
    }

    [TestMethod]
    public async Task SwitchingProvidersKeepsEachProvidersOwnFieldsAndDropsForeignOverrides()
    {
        await using var fixture = await PageFixture.CreateAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await fixture.SeedServerCatalogAsync();
        await fixture.Settings.SaveAsync(Profile, AiProfileSettings.Default with
        {
            Model = "gpt-a",
            Overrides = AiOperationOverrides.From([KeyValuePair.Create(AiOperations.BookQa, new AiOperationOverride("gpt-b", "high"))])
        }, CancellationToken.None);

        var toPersonal = fixture.Page();
        await toPersonal.OnGetAsync(CancellationToken.None);
        toPersonal.ProviderId = AiProviderIds.OpenAiCompatible;
        toPersonal.BaseUrl = "https://api.example.invalid/v1/";
        toPersonal.ModelName = "personal-model";
        toPersonal.ApiKey = "test-key";
        Assert.IsInstanceOfType<RedirectToPageResult>(await toPersonal.OnPostSaveAsync(CancellationToken.None));

        var personal = await fixture.Settings.LoadAsync(Profile, CancellationToken.None);
        Assert.AreEqual(AiProviderIds.OpenAiCompatible, personal.ProviderId);
        Assert.AreEqual("https://api.example.invalid/v1", personal.BaseUrl);
        Assert.AreEqual("personal-model", personal.Model);
        Assert.AreEqual(0, personal.Overrides.Count, "Server model ids are not carried into the personal provider.");

        var back = fixture.Page();
        await back.OnGetAsync(CancellationToken.None);
        Assert.IsNull(back.ApiKey, "The stored API key is never rendered back.");
        Assert.IsTrue(back.HasStoredApiKey);
        back.ProviderId = AiProviderIds.Server;
        back.ServerModel = "gpt-a";
        await back.OnPostSaveAsync(CancellationToken.None);

        var server = await fixture.Settings.LoadAsync(Profile, CancellationToken.None);
        Assert.AreEqual(AiProviderIds.Server, server.ProviderId);
        Assert.AreEqual("gpt-a", server.Model);
        Assert.IsNull(server.ApiKey, "The shared server connection never keeps a personal key.");
        Assert.IsNull(server.BaseUrl);
    }

    [TestMethod]
    public async Task BookPresetUsesTheCatalogAndStoresOnlyDifferences()
    {
        await using var fixture = await PageFixture.CreateAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await fixture.SeedServerCatalogAsync();
        await fixture.Settings.SaveAsync(Profile, AiProfileSettings.Default with
        {
            Model = "gpt-a",
            Overrides = AiOperationOverrides.From(
            [
                KeyValuePair.Create(AiOperations.NovelMapping, new AiOperationOverride("gpt-b", null)),
                KeyValuePair.Create(AiOperations.BookQa, new AiOperationOverride("gpt-b", "high"))
            ])
        }, CancellationToken.None);

        var page = fixture.Page();
        await page.OnGetAsync(CancellationToken.None);
        Assert.IsNotNull(page.BookPreset);
        Assert.IsFalse(page.BookPresetActive);

        page.ProviderId = AiProviderIds.Server;
        await page.OnPostApplyBookPresetAsync(CancellationToken.None);

        var saved = await fixture.Settings.LoadAsync(Profile, CancellationToken.None);
        Assert.AreEqual(AiTranslationMode.Quality, saved.TranslationMode);
        Assert.AreEqual("gpt-a", saved.Model);
        Assert.AreEqual(new AiOperationOverride("gpt-b", null), saved.Overrides.Get(AiOperations.NovelMapping), "Non-book overrides stay.");
        Assert.IsNull(saved.Overrides.Get(AiOperations.BookQa), "Book review inherits the translation settings again.");
        Assert.AreEqual(4, saved.Overrides.Count, "The mapping override plus the lighter level of analysis, memory and story context.");

        AiInvocationOptions Runs(string operation) =>
            AiOptionResolver.ResolveServer(saved.Resolve(operation), page.ServerCatalog, operation);

        Assert.AreEqual(new AiInvocationOptions("gpt-a", "medium", null, null), Runs(AiOperations.BookTranslation));
        Assert.AreEqual(new AiInvocationOptions("gpt-a", "medium", null, null), Runs(AiOperations.BookQa));
        Assert.AreEqual(new AiInvocationOptions("gpt-a", "low", null, null), Runs(AiOperations.BookAnalysis));
        Assert.AreEqual(new AiInvocationOptions("gpt-a", "low", null, null), Runs(AiOperations.BookMemory));
        Assert.AreEqual(new AiInvocationOptions("gpt-a", "low", null, null), Runs(AiOperations.StoryContext));

        var after = fixture.Page();
        await after.OnGetAsync(CancellationToken.None);
        Assert.IsTrue(after.BookPresetActive);
    }

    [TestMethod]
    public void BookPresetOnlyUsesReasoningLevelsTheModelLists()
    {
        var catalog = new AiModelCatalog(
            AiModelCatalogKeys.CodexServer,
            [new AiModelDescriptor("plain", "Plain", null, [new("high", null)], null, [], null, true, null)],
            AiModelDiscovery.Supported,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        var plan = AiBookPreset.Build(AiProfileSettings.Default, catalog)!;

        Assert.AreEqual("plain", plan.Model);
        Assert.IsNull(plan.TranslationEffort);
        Assert.IsNull(plan.LightEffort);
        Assert.AreEqual(0, plan.Settings.Overrides.Count, "Nothing is guessed when the model lists no matching level.");
        Assert.IsNull(AiBookPreset.Build(AiProfileSettings.Default, AiModelCatalog.Empty("x")), "No model, no preset.");

        var personal = AiBookPreset.Build(Personal("personal-model"), AiModelCatalog.Empty("x"))!;
        Assert.AreEqual("personal-model", personal.Model);
        Assert.AreEqual(AiTranslationMode.Quality, personal.Settings.TranslationMode);
        Assert.AreEqual(0, personal.Settings.Overrides.Count);
    }

    private static AiProfileSettings Personal(string model) =>
        new(AiProviderIds.OpenAiCompatible, "https://api.example.invalid/v1", model, "test-key", AiTranslationMode.Efficient);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class PageFixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly CountingHandler handler;
        private readonly CurrentAccountContext account;
        private readonly CodexCliProvider codex;
        private readonly AiActivityTracker tracker = new(TimeProvider.System);
        private readonly AiUsageTracker usage = new();

        private PageFixture(string root, AppDbContext db, CountingHandler handler)
        {
            this.root = root;
            this.handler = handler;
            Db = db;
            Settings = new AiProfileSettingsStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(root));
            account = new CurrentAccountContext(new FixedAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Profile), new Claim(ClaimTypes.Role, AccountRoles.Owner)],
                        "test"))
                }
            });
            codex = new CodexCliProvider(new CodexAppServerGateway(
                new CodexAppServerClient(new UnavailableLauncher(), TimeProvider.System, NullLogger<CodexAppServerClient>.Instance),
                TimeProvider.System));
        }

        public AppDbContext Db { get; }

        public AiProfileSettingsStore Settings { get; }

        public int Requests => handler.Count;

        public static async Task<PageFixture> CreateAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var root = Path.Combine(Path.GetTempPath(), $"jularr-ai-page-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "app.db")};Pooling=False")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new PageFixture(root, db, new CountingHandler(respond));
        }

        public async Task SeedServerCatalogAsync() =>
            await new AiModelCatalogStore(Db).SaveAsync(
                new AiModelCatalog(
                    AiModelCatalogKeys.CodexServer,
                    [
                        new AiModelDescriptor("gpt-a", "A", null, [new("low", null), new("medium", null), new("high", null)], "medium", [], null, false, null),
                        new AiModelDescriptor("gpt-b", "B", null, [new("low", null), new("medium", null)], "medium", [], null, true, null)
                    ],
                    AiModelDiscovery.Supported,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null),
                CancellationToken.None);

        public ProfileAiProviderRouter Router() =>
            new(
                account,
                Settings,
                usage,
                codex,
                new FixedHttpClientFactory(handler),
                new AiActivityRunner(tracker, usage, TimeProvider.System),
                new AiModelCatalogService(new AiModelCatalogStore(Db), TimeProvider.System));

        public AiSettingsModel Page()
        {
            var router = Router();
            var page = new AiSettingsModel(Db, account, Settings, router, new AiUsageStore(Db), tracker, codex, TimeProvider.System);

            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                    .BuildServiceProvider()
            };
            page.PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary<AiSettingsModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            };
            page.TempData = new TempDataDictionary(httpContext, new NoTempData());
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            codex.Dispose();
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class UnavailableLauncher : ICodexAppServerLauncher
    {
        public Task<ICodexAppServerTransport> StartAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Codex is not installed in tests.");
    }

    private sealed class FixedAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
