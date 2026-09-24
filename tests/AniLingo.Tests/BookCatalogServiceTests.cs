using AniLingo.Web.Features.Books;
using System.Net;
using System.Text;

namespace AniLingo.Tests;

[TestClass]
public sealed class BookCatalogServiceTests
{
    [TestMethod]
    public void ReadableSampleStripsGutenbergBoilerplateAndSkipsDenseContents()
    {
        var raw = """
            Project Gutenberg header
            *** START OF THE PROJECT GUTENBERG EBOOK TEST ***

            CONTENTS

            CHAPTER I. One
            CHAPTER II. Two
            CHAPTER III. Three

            CHAPTER I. One

            This is the real first chapter. It has enough prose to be readable.
            Another paragraph continues the story and establishes the actual content.
            """ + new string('x', 1200) + """

            CHAPTER II. Two

            Later chapter.
            *** END OF THE PROJECT GUTENBERG EBOOK TEST ***
            trailing license
            """;

        var sample = BookCatalogService.ExtractReadableSample(raw, 1000);

        Assert.IsTrue(sample.StartsWith("CHAPTER I. One", StringComparison.Ordinal));
        Assert.IsTrue(sample.Contains("real first chapter", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("Project Gutenberg header", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("trailing license", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReadableSampleUsesBoundedTextWhenThereAreNoChapterHeadings()
    {
        var raw = "*** START OF TEST ***\n" + new string('a', 1400) + ".\n*** END OF TEST ***";

        var sample = BookCatalogService.ExtractReadableSample(raw, 700);

        Assert.IsTrue(sample.Length <= 700);
        Assert.IsFalse(sample.Contains("*** START", StringComparison.Ordinal));
        Assert.IsFalse(sample.Contains("*** END", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SearchUsesOpenLibraryForGeneralTitles()
    {
        using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            Assert.AreEqual("openlibrary.org", request.RequestUri?.Host);

            return JsonResponse("""
                {
                  "docs": [
                    {
                      "key": "/works/OL27448W",
                      "title": "The Lord of the Rings",
                      "author_name": ["J. R. R. Tolkien"],
                      "cover_i": 14625765,
                      "first_publish_year": 1954,
                      "subject": ["Fantasy fiction", "Middle Earth"]
                    }
                  ]
                }
                """);
        }))
        {
            BaseAddress = new Uri("https://gutendex.com/")
        };

        var service = new BookCatalogService(client);
        var books = await service.SearchAsync("Herr der Ringe", CancellationToken.None);

        Assert.AreEqual(1, books.Count);
        Assert.AreEqual("ol-OL27448W", books[0].Id);
        Assert.AreEqual("The Lord of the Rings", books[0].Title);
        Assert.AreEqual("J. R. R. Tolkien", books[0].Author);
        Assert.AreEqual(1954, books[0].FirstPublishYear);
        Assert.IsFalse(books[0].CanRead);
    }

    [TestMethod]
    public async Task ReadableSampleFollowsGutenbergRedirect()
    {
        var requested = new List<string>();

        using var client = new HttpClient(new DelegateHttpMessageHandler(request =>
        {
            var uri = request.RequestUri
                ?? throw new AssertFailedException("Request URI was missing.");
            requested.Add(uri.ToString());

            if (uri.AbsolutePath == "/ebooks/2701.txt.utf-8")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri(
                    "/cache/epub/2701/pg2701.txt",
                    UriKind.Relative);
                return redirect;
            }

            if (uri.AbsolutePath == "/cache/epub/2701/pg2701.txt")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "*** START OF TEST ***\nCall me Ishmael.\n*** END OF TEST ***",
                        Encoding.UTF8,
                        "text/plain")
                };
            }

            throw new AssertFailedException($"Unexpected request: {uri}");
        }))
        {
            BaseAddress = new Uri("https://gutendex.com/")
        };

        var service = new BookCatalogService(client);
        var book = new BookCatalogItem(
            "2701",
            "Moby Dick",
            "Herman Melville",
            null,
            null,
            [],
            1851,
            "https://www.gutenberg.org/ebooks/2701.txt.utf-8",
            "https://www.gutenberg.org/ebooks/2701",
            "Project Gutenberg",
            "Project Gutenberg");

        var sample = await service.GetReadableSampleAsync(
            book,
            CancellationToken.None,
            500);

        Assert.AreEqual("Call me Ishmael.", sample);
        CollectionAssert.AreEqual(
            new[]
            {
                "https://www.gutenberg.org/ebooks/2701.txt.utf-8",
                "https://www.gutenberg.org/cache/epub/2701/pg2701.txt"
            },
            requested);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
