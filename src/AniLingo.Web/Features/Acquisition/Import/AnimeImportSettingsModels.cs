namespace AniLingo.Web.Features.Acquisition.Import;

/// <summary>
/// How a completed download's video (and its sidecars) is placed into the library. Hardlink and
/// Copy never remove the source; Move (the historical default) does. HardlinkOrCopy is an explicit
/// owner choice: try a hardlink first and fall back to a copy only when the source and the library
/// folder are on different filesystems. Plain Hardlink never falls back, so a cross-filesystem
/// import fails with a clear reason instead of silently copying.
/// </summary>
public enum AnimeImportMode
{
    Move,
    Copy,
    Hardlink,
    HardlinkOrCopy
}

/// <summary>
/// Maps a path prefix as reported by an external system (the download client's completed-download
/// path, or a Sonarr series/episode/queue path) to the equivalent local prefix AniLingo sees under
/// its own mounts. Entries are tried longest-prefix-first; a path with no matching entry is used
/// unchanged. This is the one canonical mapping for both purposes: applying it to a download path
/// and to a Sonarr-observed path keeps the two systems' view of "the same file" aligned even when
/// they mount the shared storage at different paths.
/// </summary>
public sealed record RemotePathMapping(string RemotePrefix, string LocalPrefix);

/// <summary>
/// The one canonical import-policy settings: default import mode, per-library-root overrides,
/// remote path mappings and the per-anime target root for new imports. Stored at
/// <c>/data/acquisition/import-settings.json</c> next to the other acquisition stores.
/// </summary>
public sealed record AnimeImportSettingsState(
    int Version,
    AnimeImportMode DefaultImportMode,
    Dictionary<Guid, AnimeImportMode> RootImportModes,
    List<RemotePathMapping> RemotePathMappings)
{
    public static AnimeImportSettingsState Empty() =>
        new(1, AnimeImportMode.Move, [], []);

    public AnimeImportMode ModeFor(Guid? rootId) =>
        rootId is { } id && RootImportModes.TryGetValue(id, out var mode) ? mode : DefaultImportMode;

    /// <summary>
    /// Rewrites <paramref name="path"/> using the longest matching remote prefix. Comparison is
    /// ordinal-ignore-case and tolerant of '/' vs '\\'; a path with no matching entry is returned
    /// unchanged.
    /// </summary>
    public string TranslatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        RemotePathMapping? best = null;
        foreach (var mapping in RemotePathMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.RemotePrefix))
            {
                continue;
            }

            if (!IsUnderOrEqual(path, mapping.RemotePrefix))
            {
                continue;
            }

            if (best is null || mapping.RemotePrefix.Length > best.RemotePrefix.Length)
            {
                best = mapping;
            }
        }

        if (best is null)
        {
            return path;
        }

        var remainder = path.Length > best.RemotePrefix.Length
            ? path[best.RemotePrefix.Length..]
            : "";
        var local = best.LocalPrefix.TrimEnd('/', '\\');
        return remainder.Length == 0 ? local : local + "/" + remainder.TrimStart('/', '\\').Replace('\\', '/');
    }

    private static bool IsUnderOrEqual(string path, string prefix)
    {
        var normalizedPath = path.Replace('\\', '/');
        var normalizedPrefix = prefix.Replace('\\', '/').TrimEnd('/');
        if (normalizedPath.Equals(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedPath.Length > normalizedPrefix.Length &&
               normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase) &&
               normalizedPath[normalizedPrefix.Length] == '/';
    }
}
