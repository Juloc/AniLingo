namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// Holds the one live OpenSubtitles login (runtime state, never persisted; registered as a
/// singleton). Logins are rate limited by OpenSubtitles, so concurrent callers share a single login
/// instead of each opening their own, and a session is only reused for the account and API key it
/// was issued to.
/// </summary>
public sealed class OpenSubtitlesSessionCache
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private OpenSubtitlesSession? current;

    /// <summary>
    /// The cached session when it is still valid for <paramref name="credential"/>; otherwise the
    /// result of <paramref name="login"/>, which then replaces the cache. <paramref name="renew"/>
    /// discards a session the server just rejected.
    /// </summary>
    public async Task<OpenSubtitlesSession> GetAsync(
        OpenSubtitlesCredential credential,
        DateTimeOffset now,
        bool renew,
        Func<CancellationToken, Task<OpenSubtitlesSession>> login,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!renew &&
                current is { } session &&
                session.BelongsTo(credential) &&
                session.ExpiresAtUtc > now)
            {
                return session;
            }

            current = null;
            var fresh = await login(cancellationToken);
            current = fresh;
            return fresh;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets the session, for example after the owner removed or changed the account.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            current = null;
        }
        finally
        {
            gate.Release();
        }
    }
}
