using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.Library;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Sonarr;

public sealed record SonarrConnectionSettings(string BaseUrl, string ApiKey);

public sealed record SonarrConnectionTestResult(
    bool Success,
    string Message);

public sealed record SonarrArtworkImportResult(
    int SonarrSeriesCount,
    int MatchedCount,
    int PosterCount,
    int FanartCount,
    int UnmatchedCount,
    int FailedCount,
    IReadOnlyList<string> UnmatchedTitles);

public sealed record SonarrLocalAnime(Guid Id, string Title, string Key);

public sealed record SonarrImage(
    string CoverType,
    string? Url,
    string? RemoteUrl);

public sealed record SonarrSeriesItem(
    int Id,
    string Title,
    string Path,
    IReadOnlyList<SonarrImage> Images);

public sealed record SonarrArtworkMapping(
    Guid AnimeId,
    int SonarrSeriesId,
    string SonarrTitle,
    string SonarrPath,
    DateTime UpdatedAt);

public static partial class SonarrSeriesMatcher
{
    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRegex();

    public static IReadOnlyDictionary<Guid, SonarrSeriesItem> Match(
        IReadOnlyList<SonarrLocalAnime> localAnime,
        IReadOnlyList<SonarrSeriesItem> sonarrSeries,
        IReadOnlyList<SonarrArtworkMapping> previousMappings)
    {
        var result = new Dictionary<Guid, SonarrSeriesItem>();
        var usedSeriesIds = new HashSet<int>();
        var seriesById = sonarrSeries.ToDictionary(x => x.Id);

        foreach (var local in localAnime)
        {
            var previous = previousMappings.FirstOrDefault(x => x.AnimeId == local.Id);
            if (previous is null ||
                !seriesById.TryGetValue(previous.SonarrSeriesId, out var mapped) ||
                !usedSeriesIds.Add(mapped.Id))
            {
                continue;
            }

            result[local.Id] = mapped;
        }

        foreach (var local in localAnime.Where(x => !result.ContainsKey(x.Id)))
        {
            var localKey = Normalize(local.Key);
            var localTitle = Normalize(local.Title);

            var folderMatches = sonarrSeries
                .Where(x =>
                    !usedSeriesIds.Contains(x.Id) &&
                    string.Equals(
                        Normalize(GetFolderName(x.Path)),
                        localKey,
                        StringComparison.Ordinal))
                .ToArray();

            var match = folderMatches.Length == 1
                ? folderMatches[0]
                : FindUniqueTitleMatch(
                    sonarrSeries,
                    usedSeriesIds,
                    localTitle);

            if (match is null || !usedSeriesIds.Add(match.Id))
            {
                continue;
            }

            result[local.Id] = match;
        }

        return result;
    }

    private static SonarrSeriesItem? FindUniqueTitleMatch(
        IReadOnlyList<SonarrSeriesItem> series,
        HashSet<int> usedSeriesIds,
        string localTitle)
    {
        var matches = series
            .Where(x =>
                !usedSeriesIds.Contains(x.Id) &&
                string.Equals(Normalize(x.Title), localTitle, StringComparison.Ordinal))
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string GetFolderName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var slash = Math.Max(
            trimmed.LastIndexOf('/'),
            trimmed.LastIndexOf('\\'));

        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    public static string Normalize(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Replace("×", " x ", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
        normalized = SeparatorRegex().Replace(normalized, " ");
        return SpaceRegex().Replace(normalized, " ").Trim();
    }
}

public sealed class SonarrConnectionStore(IDataProtectionProvider dataProtectionProvider)
{
    private const string RootPath = "/data/sonarr-import";
    private static readonly string ConnectionPath = Path.Combine(RootPath, "connection.json");
    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector("AniLingo.Sonarr.Connection.v1");

    public async Task<SonarrConnectionSettings?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ConnectionPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(ConnectionPath);
            var stored = await JsonSerializer.DeserializeAsync<StoredConnection>(
                stream,
                cancellationToken: cancellationToken);

            if (stored is null ||
                string.IsNullOrWhiteSpace(stored.BaseUrl) ||
                string.IsNullOrWhiteSpace(stored.ProtectedApiKey))
            {
                return null;
            }

            return new SonarrConnectionSettings(
                stored.BaseUrl,
                protector.Unprotect(stored.ProtectedApiKey));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        var temporaryPath = ConnectionPath + ".tmp";
        var stored = new StoredConnection(
            NormalizeBaseUrl(settings.BaseUrl),
            protector.Protect(settings.ApiKey));

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    stored,
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, ConnectionPath, true);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    ConnectionPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string NormalizeBaseUrl(string baseUrl) =>
        baseUrl.Trim().TrimEnd('/');

    private sealed record StoredConnection(
        string BaseUrl,
        string ProtectedApiKey);
}

public sealed class SonarrArtworkManifestStore
{
    private const string RootPath = "/data/sonarr-import";
    private static readonly string ManifestPath = Path.Combine(RootPath, "artwork-mappings.json");

    public async Task<IReadOnlyList<SonarrArtworkMapping>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(ManifestPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(ManifestPath);
            return await JsonSerializer.DeserializeAsync<List<SonarrArtworkMapping>>(
                       stream,
                       cancellationToken: cancellationToken)
                   ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<SonarrArtworkMapping> mappings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootPath);
        var temporaryPath = ManifestPath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    mappings,
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, ManifestPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed class SonarrArtworkImportService(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    ILogger logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SonarrArtworkManifestStore manifestStore = new();

    public async Task<SonarrConnectionTestResult> TestAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryGetBaseUri(settings, out var baseUri, out var error))
        {
            return new SonarrConnectionTestResult(false, error!);
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        using var request = CreateSonarrRequest(
            HttpMethod.Get,
            new Uri(baseUri, "api/v3/system/status"),
            settings.ApiKey);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new SonarrConnectionTestResult(
                    false,
                    $"Sonarr returned HTTP {(int)response.StatusCode}.");
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var status = await JsonSerializer.DeserializeAsync<SonarrSystemStatus>(
                content,
                JsonOptions,
                cancellationToken);

            return new SonarrConnectionTestResult(
                true,
                status is null
                    ? "Connected to Sonarr."
                    : $"Connected to {status.AppName ?? "Sonarr"} {status.Version ?? ""}.".Trim());
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Sonarr connection test failed for {BaseUrl}.", settings.BaseUrl);
            return new SonarrConnectionTestResult(false, "Could not reach Sonarr.");
        }
    }

    public async Task<SonarrArtworkImportResult> ImportAsync(
        SonarrConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryGetBaseUri(settings, out var baseUri, out var error))
        {
            throw new InvalidOperationException(error);
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var sonarrSeries = await LoadSeriesAsync(
            client,
            baseUri,
            settings.ApiKey,
            cancellationToken);

        var localAnime = await db.Anime
            .AsNoTracking()
            .OrderBy(x => x.Title)
            .Select(x => new SonarrLocalAnime(x.Id, x.Title, x.Key))
            .ToListAsync(cancellationToken);

        var previousMappings = await manifestStore.LoadAsync(cancellationToken);
        var matches = SonarrSeriesMatcher.Match(localAnime, sonarrSeries, previousMappings);

        var posterCount = 0;
        var fanartCount = 0;
        var failedCount = 0;
        var mappings = new List<SonarrArtworkMapping>();

        foreach (var local in localAnime)
        {
            if (!matches.TryGetValue(local.Id, out var series))
            {
                continue;
            }

            mappings.Add(new SonarrArtworkMapping(
                local.Id,
                series.Id,
                series.Title,
                series.Path,
                DateTime.UtcNow));

            var poster = series.Images.FirstOrDefault(
                x => x.CoverType.Equals("poster", StringComparison.OrdinalIgnoreCase));
            if (poster is not null)
            {
                if (await TryImportImageAsync(
                        client,
                        baseUri,
                        settings.ApiKey,
                        local.Id,
                        AnimeArtworkKind.Poster,
                        poster,
                        cancellationToken))
                {
                    posterCount++;
                }
                else
                {
                    failedCount++;
                }
            }

            var fanart = series.Images.FirstOrDefault(
                             x => x.CoverType.Equals("fanart", StringComparison.OrdinalIgnoreCase))
                         ?? series.Images.FirstOrDefault(
                             x => x.CoverType.Equals("banner", StringComparison.OrdinalIgnoreCase));

            if (fanart is not null)
            {
                if (await TryImportImageAsync(
                        client,
                        baseUri,
                        settings.ApiKey,
                        local.Id,
                        AnimeArtworkKind.Fanart,
                        fanart,
                        cancellationToken))
                {
                    fanartCount++;
                }
                else
                {
                    failedCount++;
                }
            }
        }

        await manifestStore.SaveAsync(mappings, cancellationToken);

        var unmatched = localAnime
            .Where(x => !matches.ContainsKey(x.Id))
            .Select(x => x.Title)
            .Take(12)
            .ToArray();

        return new SonarrArtworkImportResult(
            sonarrSeries.Count,
            matches.Count,
            posterCount,
            fanartCount,
            localAnime.Count - matches.Count,
            failedCount,
            unmatched);
    }

    private static async Task<IReadOnlyList<SonarrSeriesItem>> LoadSeriesAsync(
        HttpClient client,
        Uri baseUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateSonarrRequest(
            HttpMethod.Get,
            new Uri(baseUri, "api/v3/series"),
            apiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await JsonSerializer.DeserializeAsync<List<SonarrSeriesDto>>(
                       content,
                       JsonOptions,
                       cancellationToken)
                   ?? [];

        return rows
            .Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new SonarrSeriesItem(
                x.Id,
                x.Title!,
                x.Path ?? "",
                (x.Images ?? [])
                    .Where(image => !string.IsNullOrWhiteSpace(image.CoverType))
                    .Select(image => new SonarrImage(
                        image.CoverType!,
                        image.Url,
                        image.RemoteUrl))
                    .ToArray()))
            .ToArray();
    }

    private static async Task<bool> TryImportImageAsync(
        HttpClient client,
        Uri baseUri,
        string apiKey,
        Guid animeId,
        AnimeArtworkKind kind,
        SonarrImage image,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in BuildImageCandidates(baseUri, image))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
            if (IsSameServer(baseUri, candidate))
            {
                request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType?.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (await AnimeArtworkStore.SaveAsync(
                    animeId,
                    kind,
                    stream,
                    response.Content.Headers.ContentType.MediaType,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Uri> BuildImageCandidates(Uri baseUri, SonarrImage image)
    {
        if (!string.IsNullOrWhiteSpace(image.Url) &&
            Uri.TryCreate(baseUri, image.Url, out var localUri))
        {
            yield return localUri;
        }

        if (!string.IsNullOrWhiteSpace(image.RemoteUrl) &&
            Uri.TryCreate(image.RemoteUrl, UriKind.Absolute, out var remoteUri) &&
            (remoteUri.Scheme == Uri.UriSchemeHttp || remoteUri.Scheme == Uri.UriSchemeHttps))
        {
            yield return remoteUri;
        }
    }

    private static HttpRequestMessage CreateSonarrRequest(
        HttpMethod method,
        Uri uri,
        string apiKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static bool IsSameServer(Uri baseUri, Uri candidate) =>
        string.Equals(baseUri.Scheme, candidate.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(baseUri.Host, candidate.Host, StringComparison.OrdinalIgnoreCase) &&
        baseUri.Port == candidate.Port;

    private static bool TryGetBaseUri(
        SonarrConnectionSettings settings,
        out Uri baseUri,
        out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            baseUri = null!;
            error = "Sonarr API key is required.";
            return false;
        }

        var normalized = SonarrConnectionStore.NormalizeBaseUrl(settings.BaseUrl) + "/";
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsedBaseUri) ||
            (parsedBaseUri.Scheme != Uri.UriSchemeHttp &&
             parsedBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            baseUri = null!;
            error = "Sonarr URL must be an absolute http:// or https:// URL.";
            return false;
        }

        baseUri = parsedBaseUri;
        return true;
    }

    private sealed record SonarrSystemStatus(string? AppName, string? Version);

    private sealed class SonarrSeriesDto
    {
        public int Id { get; init; }
        public string? Title { get; init; }
        public string? Path { get; init; }
        public List<SonarrImageDto>? Images { get; init; }
    }

    private sealed class SonarrImageDto
    {
        public string? CoverType { get; init; }
        public string? Url { get; init; }
        public string? RemoteUrl { get; init; }
    }
}
