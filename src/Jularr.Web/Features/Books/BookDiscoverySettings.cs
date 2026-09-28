using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Jularr.Web.Features.Books;

/// <summary>
/// Owner-level Books discovery settings (#371): an optional Hardcover API token that enriches
/// Trending/Popular/New Books discovery rows for every profile with a community rating. This is
/// distinct from <see cref="BookHardcoverAccountStore"/>, which is a single profile's own personal
/// Hardcover reading-list connection. Books discovery is fully usable without this token; when it
/// is unset, Hardcover contributes nothing and discovery behaves exactly as before.
/// </summary>
public sealed record BookDiscoverySettings(string? HardcoverApiKey)
{
    public static BookDiscoverySettings Empty { get; } = new((string?)null);

    public bool HardcoverEnabled => !string.IsNullOrWhiteSpace(HardcoverApiKey);
}

public sealed class BookDiscoverySettingsStore(
    IDataProtectionProvider dataProtectionProvider,
    DirectoryInfo? settingsDirectory = null)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "Jularr.Books.Discovery.Hardcover.ApiKey.v1");
    private readonly string settingsPath = Path.Combine(
        (settingsDirectory ?? new DirectoryInfo("/data")).FullName,
        "books",
        "discovery-settings.json");

    public async Task<BookDiscoverySettings> LoadAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(settingsPath))
            {
                return BookDiscoverySettings.Empty;
            }

            var persisted = JsonSerializer.Deserialize<PersistedBookDiscoverySettings>(
                await File.ReadAllTextAsync(settingsPath, cancellationToken),
                JsonOptions);

            if (persisted is null || string.IsNullOrWhiteSpace(persisted.ProtectedHardcoverApiKey))
            {
                return BookDiscoverySettings.Empty;
            }

            return new BookDiscoverySettings(protector.Unprotect(persisted.ProtectedHardcoverApiKey));
        }
        catch (Exception exception) when (
            exception is JsonException
                or CryptographicException
                or IOException
                or UnauthorizedAccessException)
        {
            return BookDiscoverySettings.Empty;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Saves a trimmed, non-empty token; a null/blank token instead clears any saved settings.</summary>
    public async Task SaveAsync(string? hardcoverApiKey, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var trimmed = hardcoverApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                TryDelete(settingsPath);
                return;
            }

            var directory = Path.GetDirectoryName(settingsPath)
                ?? throw new InvalidOperationException("Books discovery settings directory is unavailable.");
            Directory.CreateDirectory(directory);

            var persisted = new PersistedBookDiscoverySettings(protector.Protect(trimmed));
            var temporary = settingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(
                    temporary,
                    JsonSerializer.Serialize(persisted, JsonOptions),
                    cancellationToken);
                File.Move(temporary, settingsPath, overwrite: true);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(settingsPath);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record PersistedBookDiscoverySettings(string ProtectedHardcoverApiKey);
}
