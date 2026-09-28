using System.Text.Json;
using Jularr.Web.Features.Appearance;

namespace Jularr.Web.Features.Health;

public enum UpdateCheckState
{
    NotChecked,
    UpToDate,
    UpdateAvailable,
    Unavailable
}

public sealed record UpdateStatus(
    UpdateCheckState State,
    string RunningVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    DateTimeOffset? CheckedAtUtc,
    string? Error);

/// <summary>
/// Running Jularr version vs. the latest Juloc/Jularr GitHub release (#528). This is
/// deliberately opt-in: <see cref="GetCached"/> never makes a network call, so a GET of
/// Admin/Health never blocks on GitHub. Only an explicit <see cref="RefreshAsync"/> (the owner's
/// "Check for updates" click) calls the public, unauthenticated releases API, and its result is
/// cached for <see cref="CacheDuration"/> so repeat visits/clicks do not spam GitHub. Any failure
/// (offline, rate limited, malformed response) degrades to <see cref="UpdateCheckState.Unavailable"/>
/// instead of throwing. Jularr never auto-updates itself; this only reports status (#490 covers
/// Android auto-update only).
/// </summary>
public sealed class GitHubReleaseCheckService(IHttpClientFactory httpClientFactory, TimeProvider time)
{
    public const string HttpClientName = "github-releases";
    public const string RepositoryOwner = "Juloc";
    public const string RepositoryName = "Jularr";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly object gate = new();
    private UpdateStatus? cached;

    /// <summary>The last check result, or null before the owner has ever asked for one. Never
    /// triggers a request.</summary>
    public UpdateStatus? GetCached()
    {
        lock (gate)
        {
            return cached;
        }
    }

    public async Task<UpdateStatus> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (cached is { CheckedAtUtc: { } checkedAt } existing && now - checkedAt < CacheDuration)
            {
                return existing;
            }
        }

        var result = await FetchAsync(now, cancellationToken);

        lock (gate)
        {
            cached = result;
        }

        return result;
    }

    private async Task<UpdateStatus> FetchAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = AppBuildInfo.Version;
        try
        {
            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.GetAsync(
                $"repos/{RepositoryOwner}/{RepositoryName}/releases/latest",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateStatus(
                    UpdateCheckState.Unavailable,
                    running,
                    null,
                    null,
                    now,
                    $"GitHub returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var tag = document.RootElement.TryGetProperty("tag_name", out var tagProperty)
                ? tagProperty.GetString()
                : null;
            var url = document.RootElement.TryGetProperty("html_url", out var urlProperty)
                ? urlProperty.GetString()
                : null;

            return string.IsNullOrWhiteSpace(tag)
                ? new UpdateStatus(UpdateCheckState.Unavailable, running, null, null, now, "GitHub did not report a release tag.")
                : BuildResult(running, tag, url, now);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateStatus(UpdateCheckState.Unavailable, running, null, null, now, "Could not reach GitHub.");
        }
    }

    // Jularr's alpha versions increase sequentially (0.1.0-alpha.N, tagged vN on GitHub); a plain
    // normalized-string comparison is enough to tell "different" from "same" without taking on a
    // SemVer dependency, and it never claims a downgrade the owner didn't ask about.
    public static UpdateStatus BuildResult(string running, string tag, string? url, DateTimeOffset checkedAtUtc)
    {
        var normalizedTag = tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;
        var upToDate = string.Equals(normalizedTag, running, StringComparison.OrdinalIgnoreCase);

        return new UpdateStatus(
            upToDate ? UpdateCheckState.UpToDate : UpdateCheckState.UpdateAvailable,
            running,
            normalizedTag,
            url,
            checkedAtUtc,
            null);
    }
}
