using System.Text.Json;

namespace AniLingo.Web.Features.Books;

/// <summary>
/// Books-only integration settings. SABnzbd connection settings are shared
/// with Anime and live in the canonical SABnzbd settings store
/// (Settings → SABnzbd).
/// </summary>
public sealed record BookIntegrationSettings(
    string? InboxPath)
{
    public static BookIntegrationSettings Empty { get; } =
        new((string?)null);
}

public static class BookIntegrationSettingsStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string SettingsPath =>
        Path.Combine("/data", "books", "integrations.json");

    public static BookIntegrationSettings Load(
        string? path = null)
    {
        path ??= SettingsPath;

        try
        {
            if (!File.Exists(path))
            {
                return BookIntegrationSettings.Empty;
            }

            var json = File.ReadAllText(path);
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
        CancellationToken cancellationToken,
        string? path = null)
    {
        path ??= SettingsPath;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path)
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
        finally
        {
            Gate.Release();
        }
    }

    public static BookIntegrationSettings Normalize(
        BookIntegrationSettings settings)
    {
        var inbox = Clean(
            settings.InboxPath,
            2048);

        if (inbox is not null)
        {
            inbox = Path.GetFullPath(inbox);
        }

        return new BookIntegrationSettings(inbox);
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
