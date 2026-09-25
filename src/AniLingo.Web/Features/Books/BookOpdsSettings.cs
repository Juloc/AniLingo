using System.Text.Json;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Books;

public sealed record BookOpdsSourceSettings(
    string Id,
    string Name,
    string Url,
    string? Username,
    string? Password,
    bool IsEnabled)
{
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username);
}

public static class BookOpdsSettingsStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string SettingsPath =>
        Path.Combine(
            "/data",
            "books",
            "opds-sources.json");

    public static IReadOnlyList<BookOpdsSourceSettings> Load(
        string? path = null)
    {
        path ??= SettingsPath;

        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var values = JsonSerializer.Deserialize<
                BookOpdsSourceSettings[]>(json)
                ?? [];

            return values
                .Select(NormalizeStored)
                .Where(x => x is not null)
                .Select(x => x!)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return [];
        }
    }

    public static async Task<BookOpdsSourceSettings> UpsertAsync(
        string? id,
        string name,
        string url,
        string? username,
        string? password,
        bool isEnabled,
        bool preserveExistingPassword,
        CancellationToken cancellationToken,
        string? path = null)
    {
        path ??= SettingsPath;

        var cleanId = NormalizeId(id)
            ?? Guid.NewGuid().ToString("N");
        var cleanName = CleanRequired(
            name,
            120,
            "Source name is required.");
        var cleanUrl = NormalizeUrl(url);
        var cleanUsername = CleanOptional(
            username,
            256);
        var cleanPassword = CleanOptional(
            password,
            512);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var sources = Load(path).ToList();
            var existingIndex = sources.FindIndex(x =>
                x.Id.Equals(
                    cleanId,
                    StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0
                && preserveExistingPassword
                && string.IsNullOrWhiteSpace(cleanPassword))
            {
                cleanPassword = sources[existingIndex].Password;
            }

            var updated = new BookOpdsSourceSettings(
                cleanId,
                cleanName,
                cleanUrl,
                cleanUsername,
                cleanPassword,
                isEnabled);

            if (existingIndex >= 0)
            {
                sources[existingIndex] = updated;
            }
            else
            {
                sources.Add(updated);
            }

            await SaveUnlockedAsync(
                sources,
                path,
                cancellationToken);

            return updated;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task RemoveAsync(
        string id,
        CancellationToken cancellationToken,
        string? path = null)
    {
        path ??= SettingsPath;
        var cleanId = NormalizeId(id)
            ?? throw new InvalidOperationException(
                "Invalid OPDS source id.");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var sources = Load(path)
                .Where(x => !x.Id.Equals(
                    cleanId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            await SaveUnlockedAsync(
                sources,
                path,
                cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task SaveUnlockedAsync(
        IEnumerable<BookOpdsSourceSettings> values,
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "OPDS settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var normalized = values
            .Select(NormalizeStored)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var json = JsonSerializer.Serialize(
            normalized,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            json,
            cancellationToken);

        File.Move(
            temporary,
            path,
            overwrite: true);
    }

    private static BookOpdsSourceSettings? NormalizeStored(
        BookOpdsSourceSettings source)
    {
        try
        {
            var id = NormalizeId(source.Id);
            if (id is null)
            {
                return null;
            }

            return new BookOpdsSourceSettings(
                id,
                CleanRequired(
                    source.Name,
                    120,
                    "Source name is required."),
                NormalizeUrl(source.Url),
                CleanOptional(
                    source.Username,
                    256),
                CleanOptional(
                    source.Password,
                    512),
                source.IsEnabled);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? NormalizeId(string? id)
    {
        var clean = id?.Trim().ToLowerInvariant();
        return clean is not null
            && Regex.IsMatch(
                clean,
                "^[a-f0-9]{32}$",
                RegexOptions.CultureInvariant)
            ? clean
            : null;
    }

    private static string NormalizeUrl(string url)
    {
        var clean = url?.Trim();

        if (!Uri.TryCreate(
                clean,
                UriKind.Absolute,
                out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "OPDS URL must be an absolute HTTP or HTTPS URL without embedded credentials.");
        }

        return uri.ToString();
    }

    private static string CleanRequired(
        string? value,
        int maxLength,
        string error)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            throw new InvalidOperationException(error);
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }

    private static string? CleanOptional(
        string? value,
        int maxLength)
    {
        var clean = value?.Trim();

        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }
}
