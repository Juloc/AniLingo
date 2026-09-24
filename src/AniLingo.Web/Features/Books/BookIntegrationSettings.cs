using System.Text.Json;

namespace AniLingo.Web.Features.Books;

public sealed record BookIntegrationSettings(
    string? SabnzbdBaseUrl,
    string? SabnzbdApiKey,
    string? SabnzbdCategory,
    string? InboxPath)
{
    public static BookIntegrationSettings Empty { get; } =
        new(null, null, null, null);
}

public static class BookIntegrationSettingsStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string SettingsPath =>
        Path.Combine("/data", "books", "integrations.json");

    public static BookIntegrationSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return BookIntegrationSettings.Empty;
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<BookIntegrationSettings>(json)
                ?? BookIntegrationSettings.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return BookIntegrationSettings.Empty;
        }
    }

    public static async Task SaveAsync(
        BookIntegrationSettings settings,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)
                ?? throw new InvalidOperationException(
                    "Books integration settings directory is unavailable.");
            Directory.CreateDirectory(directory);

            var normalized = Normalize(settings);
            var json = JsonSerializer.Serialize(
                normalized,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            var temporary = SettingsPath + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                json,
                cancellationToken);

            File.Move(
                temporary,
                SettingsPath,
                overwrite: true);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static BookIntegrationSettings Normalize(
        BookIntegrationSettings settings)
    {
        string? baseUrl = Clean(settings.SabnzbdBaseUrl, 2048);
        if (baseUrl is not null)
        {
            if (!Uri.TryCreate(
                    baseUrl,
                    UriKind.Absolute,
                    out var sabUri)
                || sabUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    "SABnzbd URL must be an absolute HTTP or HTTPS URL.");
            }

            baseUrl = sabUri
                .ToString()
                .TrimEnd('/');
        }

        var apiKey = Clean(
            settings.SabnzbdApiKey,
            512);
        var category = Clean(
            settings.SabnzbdCategory,
            128);
        var inbox = Clean(
            settings.InboxPath,
            2048);

        if (inbox is not null)
        {
            inbox = Path.GetFullPath(inbox);
        }

        return new BookIntegrationSettings(
            baseUrl,
            apiKey,
            category,
            inbox);
    }

    private static string? Clean(
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
