using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Jularr.Web.Features.Books;

public sealed partial class BookCatalogService
{
    private static readonly Uri HardcoverGraphQl =
        new("https://api.hardcover.app/v1/graphql");

    public async Task<string> ValidateHardcoverTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var token = accessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length < 12)
        {
            throw new InvalidOperationException(
                "Enter a valid Hardcover API token.");
        }

        using var document = await SendHardcoverAsync(
            token,
            "query JularrViewer { me { username } }",
            cancellationToken);

        var me = FirstHardcoverMe(document.RootElement);
        var username = me is { } node
            && node.TryGetProperty("username", out var usernameNode)
                ? usernameNode.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "Hardcover did not return an account for this token.");
        }

        return username.Trim();
    }

    public async Task<IReadOnlyList<BookCatalogItem>> EnrichHardcoverStatesAsync(
        IReadOnlyList<BookCatalogItem> items,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0 || string.IsNullOrWhiteSpace(accessToken))
        {
            return items;
        }

        try
        {
            using var document = await SendHardcoverAsync(
                accessToken.Trim(),
                """
                query JularrLibrary {
                  me {
                    user_books(limit: 500, order_by: { updated_at: desc }) {
                      status_id
                      book {
                        title
                        contributions { author { name } }
                        editions { isbn_10 isbn_13 }
                      }
                    }
                  }
                }
                """,
                cancellationToken);

            var me = FirstHardcoverMe(document.RootElement);
            if (me is null
                || !me.Value.TryGetProperty(
                    "user_books",
                    out var books)
                || books.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            var states = books.EnumerateArray()
                .Select(ParseHardcoverBook)
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            return items
                .Select(item =>
                {
                    var state = states.FirstOrDefault(candidate =>
                        HardcoverMatches(item, candidate));
                    return state is null
                        ? item
                        : item with
                        {
                            ExternalListState =
                                HardcoverStatusLabel(state.StatusId)
                        };
                })
                .ToArray();
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException
                or TaskCanceledException
                or InvalidOperationException
                or JsonException)
        {
            return items;
        }
    }

    private async Task<JsonDocument> SendHardcoverAsync(
        string accessToken,
        string query,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryProviderTimeout);

        using var request =
            new HttpRequestMessage(HttpMethod.Post, HardcoverGraphQl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new { query });

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (response.StatusCode is
            System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "The Hardcover API token is invalid or expired.");
        }

        response.EnsureSuccessStatusCode();
        var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);

        if (document.RootElement.TryGetProperty(
                "errors",
                out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            document.Dispose();
            throw new InvalidOperationException(
                "Hardcover could not read the account.");
        }

        return document;
    }

    private static JsonElement? FirstHardcoverMe(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("me", out var me))
        {
            return null;
        }

        if (me.ValueKind == JsonValueKind.Object)
        {
            return me;
        }

        if (me.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in me.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static HardcoverListBook? ParseHardcoverBook(
        JsonElement userBook)
    {
        if (!userBook.TryGetProperty(
                "status_id",
                out var statusNode)
            || !statusNode.TryGetInt32(out var statusId)
            || !userBook.TryGetProperty(
                "book",
                out var book)
            || book.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = book.TryGetProperty("title", out var titleNode)
            ? titleNode.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string? author = null;
        if (book.TryGetProperty(
                "contributions",
                out var contributions)
            && contributions.ValueKind == JsonValueKind.Array)
        {
            foreach (var contribution in contributions.EnumerateArray())
            {
                if (contribution.TryGetProperty(
                        "author",
                        out var authorNode)
                    && authorNode.ValueKind == JsonValueKind.Object
                    && authorNode.TryGetProperty(
                        "name",
                        out var nameNode)
                    && !string.IsNullOrWhiteSpace(nameNode.GetString()))
                {
                    author = nameNode.GetString();
                    break;
                }
            }
        }

        var isbns = new HashSet<string>(StringComparer.Ordinal);
        if (book.TryGetProperty("editions", out var editions)
            && editions.ValueKind == JsonValueKind.Array)
        {
            foreach (var edition in editions.EnumerateArray())
            {
                foreach (var key in new[] { "isbn_13", "isbn_10" })
                {
                    if (edition.TryGetProperty(key, out var isbnNode))
                    {
                        var isbn = NormalizeIsbn(isbnNode.GetString());
                        if (isbn is not null)
                        {
                            isbns.Add(isbn);
                        }
                    }
                }
            }
        }

        return new HardcoverListBook(
            title.Trim(),
            author?.Trim(),
            isbns.ToArray(),
            statusId);
    }

    private static bool HardcoverMatches(
        BookCatalogItem item,
        HardcoverListBook candidate)
    {
        var itemIsbns = (item.Isbns ?? [])
            .ToHashSet(StringComparer.Ordinal);
        if (itemIsbns.Count > 0
            && candidate.Isbns.Any(itemIsbns.Contains))
        {
            return true;
        }

        if (NormalizeForMatch(item.Title)
            != NormalizeForMatch(candidate.Title))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Author)
            || string.IsNullOrWhiteSpace(candidate.Author))
        {
            return true;
        }

        var expectedSurname = NormalizeForMatch(item.Author)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return expectedSurname is null
            || NormalizeForMatch(candidate.Author)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(expectedSurname, StringComparer.Ordinal);
    }

    private static string HardcoverStatusLabel(int statusId) =>
        statusId switch
        {
            1 => "Want to read",
            2 => "Reading",
            3 => "Read",
            4 => "Paused",
            5 => "Did not finish",
            _ => "In Hardcover"
        };

    private sealed record HardcoverListBook(
        string Title,
        string? Author,
        IReadOnlyList<string> Isbns,
        int StatusId);
}
