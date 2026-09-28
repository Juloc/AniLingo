namespace Jularr.Web.Features.Mapping;

/// <summary>
/// The six provider roles that epic #510 / issue #525 make independently assignable for anime.
/// Each role can be filled by a different provider, per work, with a global default; no single
/// "canonical AniList id" governs everything anymore.
/// </summary>
public enum MappingProviderRole
{
    /// <summary>Titles, descriptions, season/year and the display card. Today: AniList.</summary>
    DisplayMetadata,

    /// <summary>Seasons/episode numbering the library is organised by. Today: local/TVDB numbering.</summary>
    EpisodeStructure,

    /// <summary>Identity used to search for and import releases. Today: local/absolute numbering.</summary>
    AcquisitionIdentity,

    /// <summary>Where watch progress is synced. Today: AniList.</summary>
    ProgressTracking,

    /// <summary>Poster/banner artwork source. Today: AniList.</summary>
    Artwork,

    /// <summary>Extra IDs kept for cross-linking (IMDb/MAL/TMDb/TVDB). Today: TVDB from the NFO.</summary>
    CrossReferenceIds
}

/// <summary>Providers that can fill a mapping role. Stored lowercase; validated against this allowlist.</summary>
public static class MappingProviders
{
    public const string AniList = "anilist";
    public const string Tvdb = "tvdb";
    public const string Mal = "mal";
    public const string Imdb = "imdb";
    public const string Tmdb = "tmdb";

    /// <summary>Local library / absolute numbering — Jularr's own internal structure, no external provider.</summary>
    public const string Local = "local";

    public static IReadOnlyList<string> All { get; } =
        [AniList, Tvdb, Mal, Imdb, Tmdb, Local];

    public static string Normalize(string? provider) =>
        (provider ?? "").Trim().ToLowerInvariant();

    public static bool IsKnown(string? provider) =>
        All.Contains(Normalize(provider), StringComparer.Ordinal);

    /// <summary>Providers that make sense for a given role (used to validate role assignments).</summary>
    public static IReadOnlyList<string> AllowedFor(MappingProviderRole role) => role switch
    {
        // Numbering roles are only meaningful for structure-bearing sources.
        MappingProviderRole.EpisodeStructure => [Local, Tvdb, AniList],
        MappingProviderRole.AcquisitionIdentity => [Local, Tvdb, AniList],
        // Artwork/metadata/progress come from rich metadata providers.
        MappingProviderRole.DisplayMetadata => [AniList, Tvdb, Tmdb, Mal],
        MappingProviderRole.Artwork => [AniList, Tvdb, Tmdb],
        MappingProviderRole.ProgressTracking => [AniList, Mal],
        // Cross-reference IDs can name any provider.
        MappingProviderRole.CrossReferenceIds => All,
        _ => All
    };

    public static bool IsAllowedFor(MappingProviderRole role, string? provider) =>
        AllowedFor(role).Contains(Normalize(provider), StringComparer.Ordinal);
}

/// <summary>Static helpers over <see cref="MappingProviderRole"/>: ordering, stable storage keys and defaults.</summary>
public static class MappingProviderRoles
{
    public static IReadOnlyList<MappingProviderRole> All { get; } =
        Enum.GetValues<MappingProviderRole>();

    /// <summary>Stable string persisted in the role-assignment table; never localise this.</summary>
    public static string StorageKey(MappingProviderRole role) => role.ToString();

    public static bool TryParse(string? value, out MappingProviderRole role) =>
        Enum.TryParse(value, ignoreCase: false, out role) && All.Contains(role);

    /// <summary>
    /// Built-in default provider for a role. These reproduce Jularr's implicit behaviour today
    /// (docs/INFORMATION_ARCHITECTURE.md §3): AniList drives display/progress/artwork, local
    /// numbering drives structure/acquisition, TVDB carries cross-reference IDs. Assigning a role
    /// only changes which provider a feature is documented to consult; it never rewrites identity.
    /// </summary>
    public static string BuiltInDefault(MappingProviderRole role) => role switch
    {
        MappingProviderRole.DisplayMetadata => MappingProviders.AniList,
        MappingProviderRole.EpisodeStructure => MappingProviders.Local,
        MappingProviderRole.AcquisitionIdentity => MappingProviders.Local,
        MappingProviderRole.ProgressTracking => MappingProviders.AniList,
        MappingProviderRole.Artwork => MappingProviders.AniList,
        MappingProviderRole.CrossReferenceIds => MappingProviders.Tvdb,
        _ => MappingProviders.AniList
    };
}

/// <summary>One resolved role → provider assignment and whether it came from a stored override.</summary>
public sealed record ProviderRoleAssignment(
    MappingProviderRole Role,
    string Provider,
    ProviderRoleSource Source);

/// <summary>Where a resolved role assignment came from, most specific first.</summary>
public enum ProviderRoleSource
{
    /// <summary>Built-in default (nothing stored).</summary>
    BuiltIn,

    /// <summary>Stored global default for the media type.</summary>
    GlobalDefault,

    /// <summary>Stored per-work override.</summary>
    WorkOverride
}
