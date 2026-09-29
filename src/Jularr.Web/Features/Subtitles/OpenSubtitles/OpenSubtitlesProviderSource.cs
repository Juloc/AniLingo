using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// Offers OpenSubtitles as an <see cref="ISubtitleProvider"/> only while the owner has stored an
/// API key. The credential is read on every request, so saving or removing the key takes effect
/// immediately and an unconfigured server keeps showing "no subtitle providers configured".
/// </summary>
public sealed class OpenSubtitlesProviderSource(
    IProviderCredentialStore<OpenSubtitlesCredential> credentials,
    OpenSubtitlesClient client,
    ProviderResponseCache cache) : ISubtitleProviderSource
{
    public async Task<ISubtitleProvider?> GetProviderAsync(CancellationToken cancellationToken)
    {
        var credential = (await credentials.LoadAllAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.IsConfigured);

        return credential is null
            ? null
            : new OpenSubtitlesSubtitleProvider(credential, client, cache);
    }
}
