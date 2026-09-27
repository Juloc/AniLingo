using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Jularr.Web.Features.Books;

public sealed record StoredHardcoverAccount(
    int UserId,
    string Username,
    string AccessToken,
    DateTimeOffset ConnectedAt);

public sealed class BookHardcoverAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector;
    private readonly string accountDirectory;
    private static readonly SemaphoreSlim AccountGate = new(1, 1);

    public BookHardcoverAccountStore(
        IDataProtectionProvider dataProtectionProvider,
        DirectoryInfo? integrationDirectory = null)
    {
        protector = dataProtectionProvider.CreateProtector(
            "Jularr.Books.Hardcover.AccessToken.v1");
        accountDirectory = Path.Combine(
            (integrationDirectory ?? new DirectoryInfo("/data/integrations")).FullName,
            "hardcover",
            "accounts");
    }

    public async Task<StoredHardcoverAccount?> LoadAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var path = AccountPath(profileId);
        await AccountGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var persisted = JsonSerializer.Deserialize<PersistedHardcoverAccount>(
                    await File.ReadAllTextAsync(path, cancellationToken),
                    JsonOptions);
                if (persisted is null
                    || persisted.UserId <= 0
                    || string.IsNullOrWhiteSpace(persisted.Username)
                    || string.IsNullOrWhiteSpace(persisted.ProtectedAccessToken))
                {
                    return null;
                }

                return new StoredHardcoverAccount(
                    persisted.UserId,
                    persisted.Username,
                    protector.Unprotect(persisted.ProtectedAccessToken),
                    persisted.ConnectedAt);
            }
            catch (Exception exception) when (
                exception is JsonException
                    or CryptographicException
                    or IOException
                    or UnauthorizedAccessException)
            {
                return null;
            }
        }
        finally
        {
            AccountGate.Release();
        }
    }

    public async Task SaveAsync(
        string profileId,
        StoredHardcoverAccount account,
        CancellationToken cancellationToken)
    {
        var path = AccountPath(profileId);
        await AccountGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(accountDirectory);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var persisted = new PersistedHardcoverAccount(
                account.UserId,
                account.Username.Trim(),
                protector.Protect(account.AccessToken.Trim()),
                account.ConnectedAt);

            try
            {
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(persisted, JsonOptions),
                    cancellationToken);
                SetPrivateMode(temporary);
                File.Move(temporary, path, overwrite: true);
                SetPrivateMode(path);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            AccountGate.Release();
        }
    }

    public async Task DisconnectAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var path = AccountPath(profileId);
        await AccountGate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(path);
        }
        finally
        {
            AccountGate.Release();
        }
    }

    private string AccountPath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)
            || profileId.Length > 80
            || profileId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Profile ID contains unsupported characters.",
                nameof(profileId));
        }

        return Path.Combine(accountDirectory, profileId + ".json");
    }

    private static void SetPrivateMode(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record PersistedHardcoverAccount(
        int UserId,
        string Username,
        string ProtectedAccessToken,
        DateTimeOffset ConnectedAt);
}
