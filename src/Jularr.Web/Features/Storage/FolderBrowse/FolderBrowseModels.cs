using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jularr.Web.Features.Storage.FolderBrowse;

/// <summary>
/// Why a path could not be browsed, checked or created in. The browser never explains a refusal
/// with more than this code: the client turns it into a localized sentence.
/// </summary>
public enum PathProblem
{
    None,

    /// <summary>Empty, or containing a NUL or control character.</summary>
    Invalid,

    /// <summary>Relative paths are never resolved against the server's working directory.</summary>
    NotAbsolute,

    /// <summary>A <c>..</c> segment. The browser computes "up" itself and never accepts one.</summary>
    Traversal,

    /// <summary>
    /// Not on storage mounted into the container, or a symbolic link that leads out of it.
    /// </summary>
    OutsideStorage,

    NotFound,
    NotADirectory,

    /// <summary>The runtime user is not allowed to list the folder.</summary>
    NotReadable,

    /// <summary>An I/O error, a link loop or a mount that did not answer in time.</summary>
    Unavailable
}

public enum PathKind
{
    Missing,
    Directory,
    File,
    Other
}

/// <summary>One line of the container's mount table.</summary>
public sealed record MountPoint(string Path, string FileSystem, string? Source, bool ReadOnly);

/// <summary>
/// A location the browser may show: a mount point visible to the container, or the data folder.
/// <see cref="RealPath"/> is the symbolic-link-free form every containment check uses.
/// </summary>
public sealed record StorageRoot(string Path, string RealPath, string? FileSystem, bool ReadOnlyMount);

/// <summary>What the runtime user can do in a folder, as the container sees it right now.</summary>
public sealed record PathAccess(
    bool Exists,
    bool Readable,
    bool Writable,
    bool ReadOnlyMount,
    bool Unavailable);

public sealed record StorageRootEntry(string Path, string? FileSystem, PathAccess Access);

public sealed record FolderEntry(string Name, string Path, bool IsLink);

public sealed record FolderCrumb(string Name, string Path);

/// <summary>
/// The subfolders of one folder. <see cref="Parent"/> is null at the top of a storage root: "up"
/// then leads to the list of roots.
/// </summary>
public sealed record FolderListing(
    string Path,
    string? Parent,
    IReadOnlyList<FolderCrumb> Crumbs,
    PathAccess Access,
    IReadOnlyList<FolderEntry> Folders,
    bool Truncated);

public sealed record BrowseResult(PathProblem Problem, FolderListing? Listing)
{
    public bool Succeeded => Problem == PathProblem.None;
}

/// <summary>How two folders relate, for the library/inbox pair check.</summary>
public enum FolderRelation
{
    Unrelated,
    Same,
    Nested
}

/// <summary>
/// The verdict on one path. <see cref="Path"/> is the normalized form (null when the input was
/// refused before normalization); <see cref="Relation"/> is only set when a second path was given.
/// </summary>
public sealed record PathCheck(
    string? Path,
    PathProblem Problem,
    PathKind Kind,
    PathAccess Access,
    FolderRelation Relation = FolderRelation.Unrelated)
{
    public bool Exists => Kind != PathKind.Missing;
}

public enum FolderNameProblem
{
    None,
    Empty,
    TooLong,
    InvalidCharacter,
    Reserved,
    EdgeCharacter
}

public enum CreateFolderOutcome
{
    Created,
    InvalidName,
    InvalidParent,
    ReadOnly,
    NotWritable,
    AlreadyExists,
    Failed
}

public sealed record CreateFolderResult(
    CreateFolderOutcome Outcome,
    string? Path = null,
    FolderNameProblem NameProblem = FolderNameProblem.None,
    PathProblem ParentProblem = PathProblem.None)
{
    public bool Succeeded => Outcome == CreateFolderOutcome.Created;
}

public sealed record CreateFolderRequest(string? Parent, string? Name);

/// <summary>The one JSON shape (camelCase, enums as camelCase words) of every folder browse response.</summary>
public static class FolderBrowseJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
