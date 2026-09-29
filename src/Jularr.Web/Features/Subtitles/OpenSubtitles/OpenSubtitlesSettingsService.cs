using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

public enum OpenSubtitlesSaveOutcome
{
    Saved,

    /// <summary>The API key, username or password was left empty.</summary>
    MissingFields,

    /// <summary>OpenSubtitles refused the API key or the account login; nothing was saved.</summary>
    Rejected,

    /// <summary>OpenSubtitles could not be reached (or is rate limiting); nothing was saved.</summary>
    Unreachable
}

/// <summary>What the settings page shows about the OpenSubtitles connection. Never carries a secret.</summary>
public sealed record OpenSubtitlesStatus(
    bool Configured,
    string? Username,
    bool CanDownload,
    DateTimeOffset? UpdatedAtUtc,
    ProviderHealthSnapshot Health);

/// <summary>
/// The owner-facing operations behind the OpenSubtitles panel in Settings/Subtitles: validate and
/// save the API key + account, report status and framework health, and remove the connection.
/// Mirrors the Jimaku key flow (test first, then save).
/// </summary>
public sealed class OpenSubtitlesSettingsService(
    OpenSubtitlesCredentialStore store,
    OpenSubtitlesClient client,
    OpenSubtitlesSessionCache sessions,
    ProviderHealthTracker health)
{
    public async Task<OpenSubtitlesStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var credential = await store.GetAsync(cancellationToken);
        var snapshot = health.Get(ProviderKeys.OpenSubtitles);

        return credential is null || !credential.IsConfigured
            ? new OpenSubtitlesStatus(false, null, false, null, snapshot)
            : new OpenSubtitlesStatus(true, credential.Username, credential.CanDownload, credential.UpdatedAtUtc, snapshot);
    }

    public async Task<OpenSubtitlesSaveOutcome> SaveAsync(
        string? apiKey,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        // Downloads need the account login as well as the API key, so a connection without both
        // could search but never import: require the complete set.
        if (string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(password))
        {
            return OpenSubtitlesSaveOutcome.MissingFields;
        }

        var credential = new OpenSubtitlesCredential
        {
            ApiKey = apiKey.Trim(),
            Username = username.Trim(),
            Password = password
        };

        var test = await client.TestConnectionAsync(credential, cancellationToken);
        switch (test.Status)
        {
            case OpenSubtitlesConnectionStatus.Rejected:
                return OpenSubtitlesSaveOutcome.Rejected;
            case OpenSubtitlesConnectionStatus.Unreachable:
                return OpenSubtitlesSaveOutcome.Unreachable;
        }

        await store.SaveAsync(credential, cancellationToken);
        return OpenSubtitlesSaveOutcome.Saved;
    }

    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        await store.ClearAsync(cancellationToken);
        await sessions.ClearAsync(cancellationToken);
    }
}
