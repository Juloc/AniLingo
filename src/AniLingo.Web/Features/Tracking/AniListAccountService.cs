using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AniLingo.Web.Features.Tracking;

public sealed class AniListAccountException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record AniListViewer(
    int Id,
    string Name,
    string? AvatarUrl);

public sealed record AniListAccountStatus(
    bool IsConnected,
    int? ClientId,
    int? ViewerId,
    string? ViewerName,
    string? ViewerAvatarUrl,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? TokenExpiresAt)
{
    public static AniListAccountStatus Disconnected { get; } =
        new(false, null, null, null, null, null, null);

    public bool IsExpired =>
        TokenExpiresAt is not null &&
        TokenExpiresAt <= DateTimeOffset.UtcNow;
}

public sealed class AniListAccountService(
    HttpClient httpClient,
    AniListAccountStore store,
    ILogger<AniListAccountService> logger)
{
    private const string ViewerQuery = """
        query {
          Viewer {
            id
            name
            avatar { medium }
          }
        }
        """;

    public static string BuildAuthorizationUrl(int clientId)
    {
        if (clientId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientId));
        }

        return $"https://anilist.co/api/v2/oauth/authorize?client_id={clientId}&response_type=token";
    }

    public async Task<AniListAccountStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(cancellationToken);
        return stored is null
            ? AniListAccountStatus.Disconnected
            : ToStatus(stored);
    }

    public async Task<AniListAccountStatus> ConnectAsync(
        int clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (clientId <= 0)
        {
            throw new AniListAccountException("Enter a valid AniList client ID.");
        }

        var token = accessToken.Trim();
        if (token.Length < 20)
        {
            throw new AniListAccountException("The AniList access token is not valid.");
        }

        var viewer = await FetchViewerAsync(token, cancellationToken);
        var account = new StoredAniListAccount(
            clientId,
            viewer.Id,
            viewer.Name,
            viewer.AvatarUrl,
            token,
            DateTimeOffset.UtcNow,
            TryReadTokenExpiry(token));

        await store.SaveAsync(account, cancellationToken);
        return ToStatus(account);
    }

    public Task DisconnectAsync() => store.DisconnectAsync();

    private async Task<AniListViewer> FetchViewerAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new
            {
                query = ViewerQuery
            });

            using var response = await httpClient.SendAsync(
                request,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AniListAccountException(
                    $"AniList returned HTTP {(int)response.StatusCode} while validating the account.");
            }

            return ParseViewerResponse(body);
        }
        catch (AniListAccountException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            logger.LogWarning(exception, "AniList account validation failed.");
            throw new AniListAccountException(
                "AniList account validation is currently unavailable.",
                exception);
        }
    }

    public static AniListViewer ParseViewerResponse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var message = errors[0].TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            throw new AniListAccountException(
                string.IsNullOrWhiteSpace(message)
                    ? "AniList rejected the account token."
                    : $"AniList: {message}");
        }

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Viewer", out var viewer) ||
            viewer.ValueKind != JsonValueKind.Object ||
            !viewer.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id) ||
            !viewer.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new AniListAccountException(
                "AniList returned an unexpected account response.");
        }

        var name = nameElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AniListAccountException(
                "AniList returned an account without a username.");
        }

        string? avatarUrl = null;
        if (viewer.TryGetProperty("avatar", out var avatar) &&
            avatar.ValueKind == JsonValueKind.Object &&
            avatar.TryGetProperty("medium", out var medium) &&
            medium.ValueKind == JsonValueKind.String)
        {
            avatarUrl = medium.GetString();
        }

        return new AniListViewer(id, name, avatarUrl);
    }

    public static DateTimeOffset? TryReadTokenExpiry(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(
                payload.Length + ((4 - payload.Length % 4) % 4),
                '=');

            using var document = JsonDocument.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

            if (!document.RootElement.TryGetProperty("exp", out var expiry) ||
                !expiry.TryGetInt64(out var unixSeconds))
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (Exception exception) when (
            exception is FormatException or
            JsonException or
            ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static AniListAccountStatus ToStatus(StoredAniListAccount account) =>
        new(
            true,
            account.ClientId,
            account.ViewerId,
            account.ViewerName,
            account.ViewerAvatarUrl,
            account.ConnectedAt,
            account.TokenExpiresAt);
}
