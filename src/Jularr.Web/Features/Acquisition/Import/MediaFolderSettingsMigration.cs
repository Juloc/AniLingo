using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Books;

namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// One-time move of the old reading inbox into the per-media inbox folders of the canonical
/// import settings (Settings → Acquisition → Media folders). Earlier builds had one Books inbox
/// (Books → Integrations, <c>/data/books/integrations.json</c>, or <c>Books:InboxPath</c>) and read
/// Light Novel EPUBs from its <c>light-novels</c> subfolder. The Books inbox becomes the Books
/// inbox folder and <c>{inbox}/light-novels</c> the Light Novel inbox folder, unless the owner
/// already chose folders. No media is moved. Afterwards the legacy file is removed and nothing
/// reads the old keys again; the settings version records that the move ran.
/// </summary>
public static class MediaFolderSettingsMigration
{
    /// <summary>Import-settings version from which the per-media inbox folders are canonical.</summary>
    public const int MediaFoldersVersion = 2;

    public const string LegacyLightNovelFolder = "light-novels";

    public static async Task<bool> MigrateAsync(
        AnimeImportSettingsStore store,
        string? configuredBooksInbox,
        string? legacyBooksSettingsPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var legacyPath = legacyBooksSettingsPath ?? BookIntegrationSettingsStore.SettingsPath;
        var moved = false;

        var state = await store.UpdateAsync(
            current =>
            {
                if (current.Version >= MediaFoldersVersion)
                {
                    return current;
                }

                var legacyInbox = FirstNonEmpty(
                    configuredBooksInbox,
                    BookIntegrationSettingsStore.Load(legacyPath).InboxPath);
                var libraries = new Dictionary<MediaAcquisitionKind, MediaLibraryTarget>(current.MediaLibraries ?? []);
                if (legacyInbox is not null)
                {
                    var inbox = Path.GetFullPath(legacyInbox);
                    moved |= SetInboxIfEmpty(libraries, MediaAcquisitionKind.Book, inbox);
                    moved |= SetInboxIfEmpty(
                        libraries,
                        MediaAcquisitionKind.LightNovel,
                        Path.Combine(inbox, LegacyLightNovelFolder));
                }

                return current with
                {
                    Version = MediaFoldersVersion,
                    MediaLibraries = libraries
                };
            },
            cancellationToken);

        if (state.Version >= MediaFoldersVersion && File.Exists(legacyPath))
        {
            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The settings version already records the move; a leftover file is never read.
            }
        }

        return moved;
    }

    public static async Task RunAtStartupAsync(
        IServiceProvider services,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var store = services.GetRequiredService<AnimeImportSettingsStore>();
        var configuration = services.GetRequiredService<IConfiguration>();
        if (await MigrateAsync(store, configuration["Books:InboxPath"], cancellationToken: cancellationToken))
        {
            log("Moved the Books inbox into the per-media inbox folders (Settings → Acquisition → Media folders).");
        }
    }

    private static bool SetInboxIfEmpty(
        Dictionary<MediaAcquisitionKind, MediaLibraryTarget> libraries,
        MediaAcquisitionKind kind,
        string inbox)
    {
        var current = libraries.TryGetValue(kind, out var target) ? target : new MediaLibraryTarget();
        if (!string.IsNullOrWhiteSpace(current.InboxRoot))
        {
            return false;
        }

        libraries[kind] = current with { InboxRoot = inbox };
        return true;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values
            .Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
