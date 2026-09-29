using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// The OpenSubtitles.com REST API (v1): search, login and download. Every call runs through the
/// shared <see cref="ProviderExecutor"/> (#438) so timeouts, bounded retries, the 429 Retry-After
/// gate, request pacing and health tracking are the framework's, not re-implemented here. The
/// API key is sent as the <c>Api-Key</c> header and the account token as a bearer header, never in
/// a URL, and never appears in an exception message.
/// </summary>
public sealed class OpenSubtitlesClient(
    HttpClient httpClient,
    ProviderExecutor executor,
    OpenSubtitlesSessionCache sessions,
    TimeProvider clock,
    ProviderExecutionPolicy? apiPolicy = null)
{
    public static readonly Uri DefaultBaseUri = new("https://api.opensubtitles.com/api/v1/");

    /// <summary>
    /// Interactive calls: one retry for transient faults, and a gentle 4 requests/second cap well under
    /// OpenSubtitles' documented per-IP limit. A 429 records its Retry-After in the shared gate, so
    /// every later call fails fast with <see cref="ProviderRateLimitedException"/> until it elapses.
    /// </summary>
    public static readonly ProviderExecutionPolicy DefaultApiPolicy = new()
    {
        MaxAttempts = 2,
        BaseBackoff = TimeSpan.FromMilliseconds(500),
        MinSpacing = TimeSpan.FromMilliseconds(250),
        DefaultRetryAfter = TimeSpan.FromSeconds(5),
        MaxRetryAfter = TimeSpan.FromMinutes(10)
    };

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    // OpenSubtitles requires "<app name> v<version>" as the User-Agent of every request.
    private static readonly string UserAgent =
        $"Jularr v{typeof(OpenSubtitlesClient).Assembly.GetName().Version?.ToString(3) ?? "0"}";

    private readonly ProviderExecutionPolicy policy = apiPolicy ?? DefaultApiPolicy;

    // The file host is not the API: it needs no pacing, but shares the provider's rate-limit gate.
    private ProviderExecutionPolicy FilePolicy => policy with { MinSpacing = null };

    public async Task<IReadOnlyList<OpenSubtitlesSearchHit>> SearchAsync(
        OpenSubtitlesCredential credential,
        IEnumerable<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        // OpenSubtitles asks for lower-case, alphabetically ordered query parameters (it caches on them).
        var query = string.Join(
            '&',
            parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var uri = new Uri(DefaultBaseUri, $"subtitles?{query}");

        using var response = await executor.SendAsync(
            ProviderKeys.OpenSubtitles,
            httpClient,
            () => CreateRequest(HttpMethod.Get, uri, credential.ApiKey),
            policy,
            cancellationToken);
        await EnsureSuccessAsync(response, "search", cancellationToken);

        var body = await ReadJsonAsync<OpenSubtitlesSearchResponse>(response, "search", cancellationToken);
        return MapHits(body);
    }

    /// <summary>
    /// Logs in (or reuses the cached login) and asks OpenSubtitles for the download link of one file.
    /// A rejected token is renewed once. Requires the account username and password.
    /// </summary>
    public async Task<OpenSubtitlesDownloadTicket> RequestDownloadAsync(
        OpenSubtitlesCredential credential,
        long fileId,
        CancellationToken cancellationToken)
    {
        if (!credential.CanDownload)
        {
            throw new SubtitleProviderException(
                "Downloading from OpenSubtitles needs your account username and password.");
        }

        for (var attempt = 0; ; attempt++)
        {
            var session = await sessions.GetAsync(
                credential,
                clock.GetUtcNow(),
                renew: attempt > 0,
                token => LoginAsync(credential, token),
                cancellationToken);

            using var response = await executor.SendAsync(
                ProviderKeys.OpenSubtitles,
                httpClient,
                () => CreateRequest(
                    HttpMethod.Post,
                    new Uri(session.BaseUri, "download"),
                    credential.ApiKey,
                    session.Token,
                    new DownloadRequest(fileId, "srt")),
                policy,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // The token expired or was revoked server-side: log in again once.
                continue;
            }

            await EnsureSuccessAsync(response, "download", cancellationToken);
            var body = await ReadJsonAsync<OpenSubtitlesDownloadResponse>(response, "download", cancellationToken);
            return new OpenSubtitlesDownloadTicket(ValidateDownloadLink(body.Link), body.FileName);
        }
    }

    /// <summary>Fetches the subtitle file behind a <see cref="OpenSubtitlesDownloadTicket"/>.</summary>
    public async Task<byte[]> FetchFileAsync(Uri link, CancellationToken cancellationToken)
    {
        using var response = await executor.SendAsync(
            ProviderKeys.OpenSubtitles,
            httpClient,
            () =>
            {
                // The file host is given nothing but a User-Agent: no API key, no account token.
                var request = new HttpRequestMessage(HttpMethod.Get, link);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                return request;
            },
            FilePolicy,
            cancellationToken);
        await EnsureSuccessAsync(response, "file download", cancellationToken);

        if (response.Content.Headers.ContentLength is > SubtitleDownloadContent.MaxSubtitleBytes)
        {
            throw new SubtitleProviderException("OpenSubtitles returned a subtitle file that is too large.");
        }

        return await SubtitleDownloadContent.ReadLimitedBytesAsync(
                   response.Content, SubtitleDownloadContent.MaxSubtitleBytes, cancellationToken)
               ?? throw new SubtitleProviderException("OpenSubtitles returned a subtitle file that is too large.");
    }

    /// <summary>Validates the credential by logging in (always a fresh login).</summary>
    public async Task<OpenSubtitlesConnectionStatus> TestConnectionAsync(
        OpenSubtitlesCredential credential,
        CancellationToken cancellationToken)
    {
        try
        {
            await sessions.GetAsync(
                credential,
                clock.GetUtcNow(),
                renew: true,
                token => LoginAsync(credential, token),
                cancellationToken);
            return OpenSubtitlesConnectionStatus.Connected;
        }
        catch (SubtitleProviderException)
        {
            return OpenSubtitlesConnectionStatus.Rejected;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or TimeoutException
                or ProviderRateLimitedException or ProviderUnavailableException ||
            (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return OpenSubtitlesConnectionStatus.Unreachable;
        }
    }

    private async Task<OpenSubtitlesSession> LoginAsync(
        OpenSubtitlesCredential credential,
        CancellationToken cancellationToken)
    {
        using var response = await executor.SendAsync(
            ProviderKeys.OpenSubtitles,
            httpClient,
            () => CreateRequest(
                HttpMethod.Post,
                new Uri(DefaultBaseUri, "login"),
                credential.ApiKey,
                bearerToken: null,
                new LoginRequest(credential.Username, credential.Password)),
            policy,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SubtitleProviderException("OpenSubtitles rejected the account username or password.");
        }

        await EnsureSuccessAsync(response, "login", cancellationToken);
        var body = await ReadJsonAsync<OpenSubtitlesLoginResponse>(response, "login", cancellationToken);
        if (string.IsNullOrWhiteSpace(body.Token))
        {
            throw new SubtitleProviderException("OpenSubtitles did not return a login token.");
        }

        return new OpenSubtitlesSession(
            credential.Username,
            OpenSubtitlesSession.Fingerprint(credential.ApiKey),
            body.Token,
            ResolveBaseUri(body.BaseUrl),
            clock.GetUtcNow() + SessionLifetime);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri,
        string apiKey,
        string? bearerToken = null,
        object? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Accept.ParseAdd("application/json");
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody, jsonBody.GetType(), options: OpenSubtitlesJson.Options);
        }

        return request;
    }

    // Maps a non-success response onto the exception the caller (and ultimately the owner) should see.
    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = response.StatusCode;
        switch (status)
        {
            case HttpStatusCode.TooManyRequests:
                throw new ProviderRateLimitedException(
                    ProviderKeys.OpenSubtitles,
                    ProviderRetryAfter.Resolve(
                        response, clock.GetUtcNow(), policy.DefaultRetryAfter, policy.MaxRetryAfter));

            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new SubtitleProviderException(
                    $"OpenSubtitles rejected the API key or account (HTTP {(int)status}).");

            case HttpStatusCode.NotAcceptable when operation == "download":
                throw new SubtitleProviderException(await DescribeQuotaAsync(response, cancellationToken));

            case HttpStatusCode.NotFound:
                throw new SubtitleProviderException("OpenSubtitles could not find that subtitle.");

            default:
                throw new HttpRequestException($"OpenSubtitles {operation} failed with HTTP {(int)status}.", null, status);
        }
    }

    private static async Task<string> DescribeQuotaAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const string fallback = "OpenSubtitles download quota reached for this account.";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<OpenSubtitlesMessageResponse>(
                OpenSubtitlesJson.Options, cancellationToken);
            var message = new string((body?.Message ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            if (message.Length == 0)
            {
                return fallback;
            }

            return message.Length > 200 ? $"OpenSubtitles: {message[..200]}" : $"OpenSubtitles: {message}";
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(OpenSubtitlesJson.Options, cancellationToken)
                   ?? throw new SubtitleProviderException($"OpenSubtitles returned an empty {operation} response.");
        }
        catch (JsonException exception)
        {
            throw new SubtitleProviderException($"OpenSubtitles returned an unreadable {operation} response.", exception);
        }
    }

    private static IReadOnlyList<OpenSubtitlesSearchHit> MapHits(OpenSubtitlesSearchResponse response)
    {
        var hits = new List<OpenSubtitlesSearchHit>();
        foreach (var attributes in (response.Data ?? []).Select(item => item.Attributes))
        {
            // Multi-part (CD1/CD2) uploads list several files; the first part is the one to import.
            var file = attributes?.Files?
                .Where(candidate => candidate.FileId is > 0)
                .OrderBy(candidate => candidate.CdNumber ?? 1)
                .FirstOrDefault();
            if (attributes is null || file?.FileId is not { } fileId || string.IsNullOrWhiteSpace(attributes.Language))
            {
                continue;
            }

            var details = attributes.FeatureDetails;
            var release = FirstNonBlank(attributes.Release, file.FileName, details?.Title, details?.MovieName) ?? "OpenSubtitles";
            hits.Add(new OpenSubtitlesSearchHit(
                fileId,
                attributes.Language.Trim(),
                release,
                attributes.Uploader?.Name,
                attributes.ForeignPartsOnly ?? false,
                attributes.HearingImpaired ?? false,
                attributes.FromTrusted ?? false,
                attributes.AiTranslated ?? false,
                attributes.MachineTranslated ?? false,
                attributes.DownloadCount ?? 0,
                attributes.Ratings ?? 0,
                attributes.Votes ?? 0,
                attributes.UploadDate,
                FirstNonBlank(details?.ParentTitle, details?.Title, details?.MovieName),
                details?.SeasonNumber,
                details?.EpisodeNumber));
        }

        return hits;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    // The login answers with the API host to use from then on (VIP accounts get a dedicated one).
    // Only an opensubtitles.com host is followed: the token and API key must not be sent elsewhere.
    private static Uri ResolveBaseUri(string? baseUrl)
    {
        var host = (baseUrl ?? "").Trim().Replace("https://", "", StringComparison.OrdinalIgnoreCase).Trim('/');
        if (host.Length > 0 &&
            Uri.TryCreate($"https://{host}/api/v1/", UriKind.Absolute, out var candidate) &&
            IsOpenSubtitlesHost(candidate.Host, "opensubtitles.com") &&
            string.IsNullOrEmpty(candidate.UserInfo) &&
            candidate.Port == 443)
        {
            return candidate;
        }

        return DefaultBaseUri;
    }

    // The link points at the file host; refuse anything that is not an https opensubtitles site.
    private static Uri ValidateDownloadLink(string? link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            (IsOpenSubtitlesHost(uri.Host, "opensubtitles.com") || IsOpenSubtitlesHost(uri.Host, "opensubtitles.org")))
        {
            return uri;
        }

        throw new SubtitleProviderException("OpenSubtitles returned an unexpected download link.");
    }

    private static bool IsOpenSubtitlesHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private sealed record LoginRequest(string Username, string Password);

    private sealed record DownloadRequest(long FileId, string SubFormat);
}
