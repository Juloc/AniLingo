using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Jularr.Web.Features.ReadingSources;

namespace Jularr.Web.Features.ReadingDiscovery;

/// <summary>
/// One catalog search source. Implementations only use an access path the source itself
/// offers to ordinary clients (documented API or public page), identify Jularr honestly, and
/// throw <see cref="ReadingSourceUnavailableException"/> when the source cannot be used. The
/// search service isolates failures per provider and never retries a source that blocks
/// automated access.
/// </summary>
public interface IReadingCatalogProvider
{
    /// <summary>The <see cref="ReadingSourceDefinition.Key"/> this provider serves.</summary>
    string Key { get; }

    Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Shared, deliberately plain HTTP access for the catalog providers: an honest user agent,
/// bounded response size, and a fixed mapping of refusal responses to
/// <see cref="ReadingSourceUnavailableException"/>. There is no retry, header spoofing or
/// challenge handling: a source that refuses automated requests stays refused.
/// </summary>
internal static class ReadingSourceHttp
{
    public const string UserAgent =
        "Jularr/1.0 (+https://github.com/Juloc/Jularr)";

    private const int MaximumResponseBytes = 2 * 1024 * 1024;

    public static void ConfigureClient(HttpClient client) =>
        client.Timeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// GETs a page or API response as text. Returns null for HTTP 404 when
    /// <paramref name="notFoundIsEmpty"/> is set (some sites answer an empty search with 404).
    /// </summary>
    public static async Task<string?> GetStringAsync(
        HttpClient client,
        Uri uri,
        string sourceName,
        string accept,
        bool notFoundIsEmpty,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.ParseAdd(accept);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsEmpty)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Refusal(sourceName, response);
        }

        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new ReadingSourceUnavailableException(
                $"{sourceName} returned an unexpectedly large response.",
                ReadingSourceFailureKind.Unavailable);
        }

        return await ReadBoundedAsync(
            response.Content,
            cancellationToken);
    }

    internal static ReadingSourceUnavailableException Refusal(
        string sourceName,
        HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var retryAfter = ReadRetryAfter(response.Headers.RetryAfter);

        if (response.StatusCode == HttpStatusCode.TooManyRequests ||
            (response.StatusCode == HttpStatusCode.ServiceUnavailable &&
             retryAfter is not null))
        {
            return new ReadingSourceUnavailableException(
                $"{sourceName} asked Jularr to slow down (HTTP {status}).",
                ReadingSourceFailureKind.RateLimited,
                retryAfter);
        }

        // 401/403, or a bot challenge answered with a challenge marker header: the site does
        // not allow automated access. Jularr backs off and leaves it alone.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            response.Headers.Contains("cf-mitigated"))
        {
            return new ReadingSourceUnavailableException(
                $"{sourceName} does not allow automated requests (HTTP {status}).",
                ReadingSourceFailureKind.Blocked);
        }

        return new ReadingSourceUnavailableException(
            $"{sourceName} returned HTTP {status}.",
            ReadingSourceFailureKind.Unavailable);
    }

    private static TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        return null;
    }

    private static async Task<string> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumResponseBytes)
            {
                throw new ReadingSourceUnavailableException(
                    "The response was larger than Jularr accepts.",
                    ReadingSourceFailureKind.Unavailable);
            }
        }

        return ResolveEncoding(content.Headers.ContentType?.CharSet)
            .GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"', ' '));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
