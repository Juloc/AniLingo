using System.Net;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition;
using Jularr.Web.Features.Acquisition.Health;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Books;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

[TestClass]
public sealed class BookSearchCoordinatorTests
{
    [TestMethod]
    public async Task SearchCombinesEnabledOpdsAndUsenetAvailabilityIntoOneWork()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "jularr-book-search-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settingsPath = Path.Combine(root, "opds-sources.json");
            var dbPath = Path.Combine(root, "search.db");
            await using var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options);

            using var client = new HttpClient(
                new DelegateHttpMessageHandler(request =>
                {
                    var uri = request.RequestUri
                        ?? throw new AssertFailedException("Request URI was missing.");

                    if (uri.Host == "catalog.example")
                    {
                        return JsonResponse(
                            """
                            {
                              "metadata": { "title": "Test OPDS" },
                              "publications": [
                                {
                                  "metadata": {
                                    "title": "Treasure Island",
                                    "author": [{"name": "Robert Louis Stevenson"}],
                                    "description": "Pirates and treasure.",
                                    "language": "en"
                                  },
                                  "links": [
                                    {
                                      "rel": "http://opds-spec.org/acquisition/open-access",
                                      "type": "application/epub+zip",
                                      "href": "/books/treasure.epub"
                                    }
                                  ]
                                }
                              ]
                            }
                            """);
                    }

                    // The metadata catalogs are deliberately unavailable. OPDS must still produce
                    // the canonical search result instead of the whole search failing.
                    throw new HttpRequestException("Catalog unavailable in test.");
                }))
            {
                BaseAddress = new Uri("https://gutendex.com/")
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Books:Opds:SettingsPath"] = settingsPath
                    })
                .Build();
            var books = new BookCatalogService(
                client,
                db,
                new NoopBookTranslator(),
                configuration);

            await books.SaveOpdsSourceAsync(
                null,
                "Test OPDS",
                "https://catalog.example/opds",
                null,
                null,
                true,
                false,
                CancellationToken.None);

            var protection = new EphemeralDataProtectionProvider();
            var indexerStore = new IndexerStore(
                protection,
                new DirectoryInfo(root));
            await indexerStore.SaveAsync(
                new IndexerEntry(
                    Guid.NewGuid(),
                    "Books Indexer",
                    IndexerType.Newznab,
                    Enabled: true,
                    Priority: 1,
                    IndexerSettings.CreateDefault(
                        "https://indexer.example",
                        IndexerType.Newznab),
                    "secret"));

            var fakeIndexer = new FakeIndexer();
            var indexers = new IndexerSearchCoordinator(
                new Dictionary<IndexerType, IIndexer>
                {
                    [IndexerType.Newznab] = fakeIndexer
                },
                indexerStore,
                new AcquisitionHealthStore(new DirectoryInfo(root)),
                NullLogger<IndexerSearchCoordinator>.Instance);
            var coordinator = new BookSearchCoordinator(
                books,
                indexers,
                NullLogger<BookSearchCoordinator>.Instance);

            var response = await coordinator.SearchAsync(
                "Treasure Island",
                CancellationToken.None);

            var result = Assert.ContainsSingle(response.Items);
            Assert.AreEqual("Treasure Island", result.Book.Title);
            Assert.AreEqual("Robert Louis Stevenson", result.Book.Author);
            Assert.IsTrue(result.Book.Identities.Any(identity =>
                identity.StartsWith("opds-", StringComparison.Ordinal)));
            Assert.IsFalse(result.Availability.DirectOrFree);
            Assert.IsTrue(result.Availability.Opds);
            Assert.IsTrue(result.Availability.Usenet);
            Assert.AreEqual(1, result.Availability.EligibleUsenetReleases);
            Assert.IsTrue(fakeIndexer.Searches > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/opds+json")
        };

    private sealed class FakeIndexer : IIndexer
    {
        public int Searches { get; private set; }

        public IndexerType Type => IndexerType.Newznab;

        public Task<IndexerConnectionTestResult> TestAsync(
            IndexerEntry entry,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new IndexerConnectionTestResult(true));

        public Task<IReadOnlyList<ProwlarrReleaseCandidate>> SearchAsync(
            IndexerEntry entry,
            IndexerSearchQuery query,
            CancellationToken cancellationToken)
        {
            Searches++;
            IReadOnlyList<ProwlarrReleaseCandidate> releases =
            [
                new ProwlarrReleaseCandidate(
                    "Treasure Island EPUB",
                    entry.Name,
                    1,
                    "usenet",
                    2_000_000,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    1,
                    1,
                    "treasure-island-epub",
                    null,
                    AnimeReleaseParser.Parse("Treasure Island EPUB"),
                    [],
                    new Uri("https://indexer.example/treasure-island.nzb"),
                    null)
            ];
            return Task.FromResult(releases);
        }
    }

    private sealed class NoopBookTranslator : IBookTranslator
    {
        public string Id => "noop-book-search";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            Task.FromResult(sourceText);
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
