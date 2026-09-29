namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// The media types a profile can add to the server. A media type is registered on the shared
/// acquisition spine by its value here plus: a download-client category
/// (<c>DownloadClientSettings.Categories</c>), an <c>ICompletedDownloadImportAdapter</c> behind the
/// completed-download dispatcher, and optionally folders and remote path mappings
/// (<c>MediaLibraryTarget</c>). Everything that is per media type (categories, folders, path
/// mappings) is keyed by this enum and picks up a new value without a new setting.
/// </summary>
public enum MediaAcquisitionKind
{
    Anime,
    Manga,
    LightNovel,
    Book
}

/// <summary>What a non-owner user may do when adding something from search.</summary>
public enum UserAddMode
{
    /// <summary>Users cannot add or request; only the owner adds.</summary>
    Disabled,

    /// <summary>Users create a request the owner approves or rejects.</summary>
    Request,

    /// <summary>Users start the automatic acquisition directly.</summary>
    Automatic
}

/// <summary>Who may use the manual add controls (file upload, URL, NZB, inbox import).</summary>
public enum ManualAddMode
{
    OwnerOnly,
    Users
}

public enum AcquisitionRequestStatus
{
    /// <summary>Waiting for the owner.</summary>
    Pending,

    /// <summary>Accepted; waiting for acquisition to start or for the owner to add it by hand.</summary>
    Approved,

    Searching,
    Downloading,
    Importing,
    Completed,
    Rejected,
    Failed
}

/// <summary>The owner's rule for one media type. Owners themselves always add automatically and manually.</summary>
public sealed record AcquisitionAccessPolicy(
    MediaAcquisitionKind Kind,
    UserAddMode UserAdd,
    ManualAddMode Manual)
{
    public static AcquisitionAccessPolicy Default(MediaAcquisitionKind kind) =>
        new(kind, UserAddMode.Request, ManualAddMode.OwnerOnly);
}

/// <summary>What the current profile may do for one media type — the only thing pages check.</summary>
public sealed record AcquisitionCapabilities(
    MediaAcquisitionKind Kind,
    bool CanAdd,
    bool AddCreatesRequest,
    bool CanAddManually,
    bool IsOwner)
{
    public static AcquisitionCapabilities Resolve(AcquisitionAccessPolicy policy, bool isOwner) =>
        isOwner
            ? new(policy.Kind, CanAdd: true, AddCreatesRequest: false, CanAddManually: true, IsOwner: true)
            : new(
                policy.Kind,
                CanAdd: policy.UserAdd != UserAddMode.Disabled,
                AddCreatesRequest: policy.UserAdd == UserAddMode.Request,
                CanAddManually: policy.Manual == ManualAddMode.Users,
                IsOwner: false);
}

/// <summary>
/// One wish to have a title on the server, from any profile. The same row carries the owner's
/// decision and the acquisition progress, so there is one place to see what was asked for and
/// what happened to it.
/// </summary>
public sealed record AcquisitionRequest(
    Guid Id,
    MediaAcquisitionKind Kind,
    string Provider,
    string ExternalId,
    string Title,
    string? Subtitle,
    string? CoverImageUrl,
    string? PayloadJson,
    string RequestedByProfileId,
    AcquisitionRequestStatus Status,
    string? StatusMessage,
    Guid? OperationId,
    string? ResultUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? DecidedByProfileId,
    DateTime? DecidedAt)
{
    public bool IsOpen => Status is AcquisitionRequestStatus.Pending
        or AcquisitionRequestStatus.Approved
        or AcquisitionRequestStatus.Searching
        or AcquisitionRequestStatus.Downloading
        or AcquisitionRequestStatus.Importing;
}

/// <summary>What a page submits when a profile adds or requests a title found in search.</summary>
public sealed record AcquisitionRequestDraft(
    MediaAcquisitionKind Kind,
    string Provider,
    string ExternalId,
    string Title,
    string? Subtitle,
    string? CoverImageUrl,
    string? PayloadJson = null);

public sealed record AcquisitionExecution(
    AcquisitionRequestStatus Status,
    string? Message,
    Guid? OperationId = null,
    string? ResultUrl = null);

/// <summary>Starts the automatic acquisition for one media type.</summary>
public interface IAcquisitionRequestExecutor
{
    MediaAcquisitionKind Kind { get; }

    Task<AcquisitionExecution> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken);
}

public sealed class AcquisitionAccessDeniedException(string message) : Exception(message);

public static class AcquisitionAccessNames
{
    public static string Kind(MediaAcquisitionKind kind) => kind switch
    {
        MediaAcquisitionKind.Anime => "anime",
        MediaAcquisitionKind.Manga => "manga",
        MediaAcquisitionKind.LightNovel => "lightNovel",
        MediaAcquisitionKind.Book => "book",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static MediaAcquisitionKind ParseKind(string value) => value switch
    {
        "anime" => MediaAcquisitionKind.Anime,
        "manga" => MediaAcquisitionKind.Manga,
        "lightNovel" => MediaAcquisitionKind.LightNovel,
        "book" => MediaAcquisitionKind.Book,
        _ => throw new ArgumentException($"Unknown media kind '{value}'.", nameof(value))
    };

    public static string UserAdd(UserAddMode mode) => mode switch
    {
        UserAddMode.Disabled => "disabled",
        UserAddMode.Request => "request",
        UserAddMode.Automatic => "automatic",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static UserAddMode ParseUserAdd(string value) => value switch
    {
        "disabled" => UserAddMode.Disabled,
        "request" => UserAddMode.Request,
        "automatic" => UserAddMode.Automatic,
        _ => throw new ArgumentException($"Unknown user add mode '{value}'.", nameof(value))
    };

    public static string Manual(ManualAddMode mode) => mode switch
    {
        ManualAddMode.OwnerOnly => "ownerOnly",
        ManualAddMode.Users => "users",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static ManualAddMode ParseManual(string value) => value switch
    {
        "ownerOnly" => ManualAddMode.OwnerOnly,
        "users" => ManualAddMode.Users,
        _ => throw new ArgumentException($"Unknown manual add mode '{value}'.", nameof(value))
    };

    public static string Status(AcquisitionRequestStatus status) => status switch
    {
        AcquisitionRequestStatus.Pending => "pending",
        AcquisitionRequestStatus.Approved => "approved",
        AcquisitionRequestStatus.Searching => "searching",
        AcquisitionRequestStatus.Downloading => "downloading",
        AcquisitionRequestStatus.Importing => "importing",
        AcquisitionRequestStatus.Completed => "completed",
        AcquisitionRequestStatus.Rejected => "rejected",
        AcquisitionRequestStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static AcquisitionRequestStatus ParseStatus(string value) => value switch
    {
        "pending" => AcquisitionRequestStatus.Pending,
        "approved" => AcquisitionRequestStatus.Approved,
        "searching" => AcquisitionRequestStatus.Searching,
        "downloading" => AcquisitionRequestStatus.Downloading,
        "importing" => AcquisitionRequestStatus.Importing,
        "completed" => AcquisitionRequestStatus.Completed,
        "rejected" => AcquisitionRequestStatus.Rejected,
        "failed" => AcquisitionRequestStatus.Failed,
        _ => throw new ArgumentException($"Unknown request status '{value}'.", nameof(value))
    };
}
