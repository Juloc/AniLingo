using Jularr.Web.Features.Acquisition.Access;

namespace Jularr.Web.Features.Acquisition.Import;

/// <summary>
/// One-time move of the global remote path mapping list into the per-media-type shape
/// (<see cref="MediaLibraryTarget.RemotePathMappings"/>). Before mappings belonged to a media
/// type, one list translated every completed-download path and every Sonarr-observed path, so
/// each media type receives a copy of the whole list and the translation of existing setups does
/// not change; the owner can then prune a media type's list. The store applies this when it reads a
/// file that still carries the legacy list, writes the result back and never keeps the legacy list
/// afterwards.
/// </summary>
public static class RemotePathMappingMigration
{
    public static bool IsNeeded(AnimeImportSettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.LegacyRemotePathMappings is not null;
    }

    public static AnimeImportSettingsState Migrate(AnimeImportSettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.LegacyRemotePathMappings is not { } legacy)
        {
            return state;
        }

        var mappings = legacy
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.RemotePrefix) &&
                !string.IsNullOrWhiteSpace(mapping.LocalPrefix))
            .ToArray();
        var libraries = new Dictionary<MediaAcquisitionKind, MediaLibraryTarget>(state.MediaLibraries ?? []);
        if (mappings.Length > 0)
        {
            foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
            {
                var target = libraries.TryGetValue(kind, out var existing) ? existing : new MediaLibraryTarget();
                var merged = (target.RemotePathMappings ?? []).ToList();
                foreach (var mapping in mappings)
                {
                    if (!merged.Any(item => item.RemotePrefix.Equals(mapping.RemotePrefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        merged.Add(mapping);
                    }
                }

                libraries[kind] = target with { RemotePathMappings = merged };
            }
        }

        return state with
        {
            MediaLibraries = libraries,
            LegacyRemotePathMappings = null
        };
    }
}
