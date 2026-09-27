using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Provider-neutral codes for a profile's personal reading-list state
/// (<see cref="BookCatalogItem.ExternalListState"/>); the UI localizes them.
/// </summary>
public static class BookListStates
{
    public const string WantToRead = "want-to-read";
    public const string Reading = "reading";
    public const string Read = "read";
    public const string Paused = "paused";
    public const string DidNotFinish = "did-not-finish";

    public static readonly IReadOnlyList<string> All = [WantToRead, Reading, Read, Paused, DidNotFinish];
}

/// <summary>Why a Hardcover connection could not be made; the page shows a localized reason.</summary>
public sealed class HardcoverConnectionException(string message)
    : InvalidOperationException(message);

/// <summary>A connected Hardcover account: who it is, as Hardcover names it.</summary>
public sealed record HardcoverViewer(int UserId, string Username);

/// <summary>
/// Optional Hardcover integration (#371): a profile may connect its own Hardcover account to see
/// its personal reading-list state on book results. Hardcover is never a metadata provider or a
/// second library; search and the local library work without it.
/// </summary>
public sealed partial class BookCatalogService
{
    /// <summary>Where a profile creates a read-only personal access token with the scopes Jularr needs.</summary>
    public const string HardcoverNewTokenUrl =
        "https://hardcover.app/account/api/keys/new?scope=read:me+read:library+read:catalog";

    private const int HardcoverListLimit = 500;
    private const int HardcoverListCacheLimit = 64;
    private static readonly Uri HardcoverGraphQl = new("https://api.hardcover.app/v1/graphql");
    private static readonly TimeSpan HardcoverListCacheLifetime = TimeSpan.FromMinutes(3);

    // A profile's list is read once every few minutes, not on every search (Hardcover limits
    // requests per minute). Keyed by a hash of the token, so it is profile-scoped and never
    // outlives a disconnect or a new token.
    private static readonly ConcurrentDictionary<string, (DateTimeOffset LoadedAt, HardcoverListBook[] Books)> HardcoverLists =
        new(StringComparer.Ordinal);

    /// <summary>The token as Hardcover expects it: trimmed, without a pasted "Bearer " prefix.</summary>
    public static string NormalizeHardcoverToken(string? accessToken)
    {
        var token = accessToken?.Trim() ?? "";
        return token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token["Bearer ".Length..].Trim()
            : token;
    }

    /// <summary>Checks a personal access token and returns the account it belongs to.</summary>
    /// <exception cref="HardcoverConnectionException">Hardcover refused the token.</exception>
    /// <exception cref="HttpRequestException">Hardcover could not be reached.</exception>
    public async Task<HardcoverViewer> ValidateHardcoverTokenAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var token = NormalizeHardcoverToken(accessToken);
        if (token.Length < 12 || token.Any(char.IsWhiteSpace))
        {
            throw new HardcoverConnectionException("The Hardcover token is malformed.");
        }

        using var document = await SendHardcoverAsync(
            token,
            "query JularrViewer { me { id username } }",
            variables: null,
            cancellationToken);

        if (FirstHardcoverMe(document.RootElement) is not { } viewer
            || !viewer.TryGetProperty("id", out var idNode)
            || !idNode.TryGetInt32(out var userId)
            || !viewer.TryGetProperty("username", out var usernameNode)
            || usernameNode.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(usernameNode.GetString()))
        {
            throw new HardcoverConnectionException("Hardcover did not return an account for this token.");
        }

        return new HardcoverViewer(userId, usernameNode.GetString()!.Trim());
    }

    /// <summary>
    /// Adds the connected profile's Hardcover list state to <paramref name="items"/>. Any
    /// Hardcover failure leaves the results unchanged.
    /// </summary>
    public async Task<IReadOnlyList<BookCatalogItem>> EnrichHardcoverStatesAsync(
        IReadOnlyList<BookCatalogItem> items,
        StoredHardcoverAccount? account,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0 || account is null || string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return items;
        }

        try
        {
            var books = await LoadHardcoverListAsync(account, cancellationToken);
            return items
                .Select(item =>
                {
                    var state = books.FirstOrDefault(candidate => HardcoverMatches(item, candidate));
                    return state is null || HardcoverState(state.StatusId) is not { } code
                        ? item
                        : item with { ExternalListState = code };
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

    private async Task<HardcoverListBook[]> LoadHardcoverListAsync(
        StoredHardcoverAccount account,
        CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.AccessToken)));
        if (HardcoverLists.TryGetValue(key, out var cached)
            && DateTimeOffset.UtcNow - cached.LoadedAt < HardcoverListCacheLifetime)
        {
            return cached.Books;
        }

        // Kept shallow: the shelf entries with the work's title and cached authors, and the
        // ISBNs of the shelved edition.
        using var document = await SendHardcoverAsync(
            account.AccessToken,
            """
            query JularrLibrary($userId: Int!, $limit: Int!) {
              user_books(
                where: { user_id: { _eq: $userId } }
                limit: $limit
                order_by: { updated_at: desc }
              ) {
                status_id
                book { title cached_contributors }
                edition { isbn_10 isbn_13 }
              }
            }
            """,
            new Dictionary<string, object> { ["userId"] = account.UserId, ["limit"] = HardcoverListLimit },
            cancellationToken);

        var books = document.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("user_books", out var userBooks)
            && userBooks.ValueKind == JsonValueKind.Array
                ? userBooks.EnumerateArray().Select(ParseHardcoverBook).OfType<HardcoverListBook>().ToArray()
                : [];

        if (HardcoverLists.Count >= HardcoverListCacheLimit)
        {
            HardcoverLists.Clear();
        }

        HardcoverLists[key] = (DateTimeOffset.UtcNow, books);
        return books;
    }

    private async Task<JsonDocument> SendHardcoverAsync(
        string accessToken,
        string query,
        IReadOnlyDictionary<string, object>? variables,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryProviderTimeout);

        using var request =
            new HttpRequestMessage(HttpMethod.Post, HardcoverGraphQl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", NormalizeHardcoverToken(accessToken));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new { query, variables });

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (response.StatusCode is
            System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
        {
            throw new HardcoverConnectionException("The Hardcover token is invalid, expired or lacks a scope.");
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
            throw new HardcoverConnectionException("Hardcover could not read the account.");
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

        // "me" is a list holding the one signed-in user.
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
        if (!userBook.TryGetProperty("status_id", out var statusNode)
            || !statusNode.TryGetInt32(out var statusId)
            || !userBook.TryGetProperty("book", out var book)
            || book.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = book.TryGetProperty("title", out var titleNode)
            && titleNode.ValueKind == JsonValueKind.String
                ? titleNode.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // cached_contributors: [{ "author": { "name": … }, "contribution": null | "Editor" | … }];
        // a contributor without a contribution role is an author.
        string? author = null;
        if (book.TryGetProperty("cached_contributors", out var contributors)
            && contributors.ValueKind == JsonValueKind.Array)
        {
            author = contributors.EnumerateArray()
                .Where(contributor => contributor.ValueKind == JsonValueKind.Object)
                .OrderBy(contributor =>
                    contributor.TryGetProperty("contribution", out var role)
                    && role.ValueKind == JsonValueKind.String
                        ? 1
                        : 0)
                .Select(contributor =>
                    contributor.TryGetProperty("author", out var authorNode)
                    && authorNode.ValueKind == JsonValueKind.Object
                    && authorNode.TryGetProperty("name", out var nameNode)
                    && nameNode.ValueKind == JsonValueKind.String
                        ? nameNode.GetString()
                        : null)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        }

        var isbns = new HashSet<string>(StringComparer.Ordinal);
        if (userBook.TryGetProperty("edition", out var edition)
            && edition.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "isbn_13", "isbn_10" })
            {
                if (edition.TryGetProperty(key, out var isbnNode)
                    && isbnNode.ValueKind == JsonValueKind.String
                    && BookWorkSearch.NormalizeIsbn(isbnNode.GetString()) is { } isbn)
                {
                    isbns.Add(isbn);
                }
            }
        }

        return new HardcoverListBook(title.Trim(), author?.Trim(), isbns, statusId);
    }

    /// <summary>The same book by a shared ISBN, or by the search's own work identity rule.</summary>
    private static bool HardcoverMatches(
        BookCatalogItem item,
        HardcoverListBook candidate) =>
        (candidate.Isbns.Count > 0 && item.Isbns.Any(candidate.Isbns.Contains))
        || BookWorkSearch.SameWork(item.Title, item.Author, candidate.Title, candidate.Author);

    private static string? HardcoverState(int statusId) =>
        statusId switch
        {
            1 => BookListStates.WantToRead,
            2 => BookListStates.Reading,
            3 => BookListStates.Read,
            4 => BookListStates.Paused,
            5 => BookListStates.DidNotFinish,
            // 6 is "Ignored": the reader chose not to see the book; not a list state.
            _ => null
        };

    private sealed record HardcoverListBook(
        string Title,
        string? Author,
        IReadOnlySet<string> Isbns,
        int StatusId);
}
