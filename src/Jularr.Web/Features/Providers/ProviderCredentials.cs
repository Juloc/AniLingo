using Microsoft.AspNetCore.DataProtection;

namespace Jularr.Web.Features.Providers;

/// <summary>
/// The credential contract for external providers. Jularr already protects
/// provider secrets with ASP.NET Core Data Protection (see
/// <c>IndexerStore</c>, <c>DownloadClientStore</c>, <c>AniListAccountStore</c>);
/// this is the shared convention new provider families (#560 subtitles, #440
/// audiobooks) follow instead of inventing another secret store:
/// <list type="bullet">
///   <item>Non-secret configuration lives in a JSON settings store under <c>/data</c>.</item>
///   <item>Each secret field is encrypted at rest with a Data Protection protector
///   obtained from <see cref="ProtectorFor"/>, so purpose strings never collide across
///   providers.</item>
///   <item>The in-memory credential model exposes the plaintext secret with
///   <c>[JsonIgnore]</c> so it is never written to the JSON file unprotected.</item>
/// </list>
/// Existing stores keep their original purpose strings for data-at-rest
/// compatibility; new providers should adopt <see cref="ProtectorFor"/>.
/// </summary>
public static class ProviderCredentials
{
    /// <summary>
    /// A Data Protection protector scoped to one provider's credentials. The purpose string is
    /// derived from the stable provider key so two providers never share a protector.
    /// </summary>
    public static IDataProtector ProtectorFor(
        IDataProtectionProvider dataProtectionProvider,
        string providerKey,
        string version = "v1")
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        return dataProtectionProvider.CreateProtector($"Jularr.Providers.{providerKey}.Credentials.{version}");
    }
}

/// <summary>
/// A store that persists one provider family's credentials. Implementations encrypt
/// secrets with a <see cref="ProviderCredentials.ProtectorFor"/> protector and keep
/// non-secret configuration as JSON under <c>/data</c>. <typeparamref name="TCredential"/>
/// is the in-memory model (plaintext secret marked <c>[JsonIgnore]</c>).
/// </summary>
public interface IProviderCredentialStore<TCredential>
{
    Task<IReadOnlyList<TCredential>> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TCredential credential, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
