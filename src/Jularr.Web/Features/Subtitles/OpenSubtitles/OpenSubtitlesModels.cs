using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// The owner's OpenSubtitles.com REST API access. <see cref="Username"/> is non-secret and lives in
/// the JSON settings store; the API key and password are secrets, encrypted at rest by
/// <see cref="OpenSubtitlesCredentialStore"/> and exposed here as plaintext only in memory
/// (<see cref="JsonIgnoreAttribute"/> keeps them out of any serialization of this model).
/// </summary>
public sealed record OpenSubtitlesCredential
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Username { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The consumer API key the owner registered on opensubtitles.com; required for every call.</summary>
    [JsonIgnore]
    public string ApiKey { get; init; } = "";

    /// <summary>The account password; only used to obtain the bearer token downloads require.</summary>
    [JsonIgnore]
    public string Password { get; init; } = "";

    /// <summary>Searching only needs the API key, so the provider is offered as soon as one is present.</summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Downloads additionally need an account login (OpenSubtitles issues the download token on login).</summary>
    [JsonIgnore]
    public bool CanDownload =>
        IsConfigured && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>An authenticated OpenSubtitles session: the bearer token and the API host the login answered with.</summary>
public sealed record OpenSubtitlesSession(
    string Username,
    string ApiKeyFingerprint,
    string Token,
    Uri BaseUri,
    DateTimeOffset ExpiresAtUtc,
    int? AllowedDownloads)
{
    /// <summary>A short, non-reversible marker of the API key so a saved key change invalidates the session.</summary>
    public static string Fingerprint(string apiKey) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)))[..16];

    public bool BelongsTo(OpenSubtitlesCredential credential) =>
        string.Equals(Username, credential.Username, StringComparison.OrdinalIgnoreCase) &&
        ApiKeyFingerprint == Fingerprint(credential.ApiKey);
}

/// <summary>One search candidate as OpenSubtitles reports it, before it is mapped onto Jularr's result model.</summary>
public sealed record OpenSubtitlesSearchHit(
    long FileId,
    string Language,
    string Release,
    string? Uploader,
    bool ForeignPartsOnly,
    bool HearingImpaired,
    bool FromTrusted,
    bool AiTranslated,
    bool MachineTranslated,
    int DownloadCount,
    double Ratings,
    int Votes,
    DateTimeOffset? UploadedAt,
    string? Title,
    int? SeasonNumber,
    int? EpisodeNumber);

/// <summary>A resolved download: where the subtitle file lives and what OpenSubtitles calls it.</summary>
public sealed record OpenSubtitlesDownloadTicket(
    Uri Link,
    string? FileName);

/// <summary>Outcome of validating an account against OpenSubtitles.</summary>
public enum OpenSubtitlesConnectionStatus
{
    Connected,
    Rejected,
    Unreachable
}

public sealed record OpenSubtitlesConnectionResult(OpenSubtitlesConnectionStatus Status, int? AllowedDownloads = null);

// --- Wire models (only the fields Jularr reads; the API adds more). ----------------------------

internal static class OpenSubtitlesJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

internal sealed class OpenSubtitlesSearchResponse
{
    public List<OpenSubtitlesSearchItem>? Data { get; set; }
}

internal sealed class OpenSubtitlesSearchItem
{
    public OpenSubtitlesAttributes? Attributes { get; set; }
}

internal sealed class OpenSubtitlesAttributes
{
    public string? Language { get; set; }

    public int? DownloadCount { get; set; }

    public bool? HearingImpaired { get; set; }

    public bool? ForeignPartsOnly { get; set; }

    public bool? FromTrusted { get; set; }

    public bool? AiTranslated { get; set; }

    public bool? MachineTranslated { get; set; }

    public double? Ratings { get; set; }

    public int? Votes { get; set; }

    public DateTimeOffset? UploadDate { get; set; }

    public string? Release { get; set; }

    public OpenSubtitlesUploader? Uploader { get; set; }

    public OpenSubtitlesFeatureDetails? FeatureDetails { get; set; }

    public List<OpenSubtitlesFile>? Files { get; set; }
}

internal sealed class OpenSubtitlesUploader
{
    public string? Name { get; set; }
}

internal sealed class OpenSubtitlesFeatureDetails
{
    public string? Title { get; set; }

    public string? ParentTitle { get; set; }

    public string? MovieName { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }
}

internal sealed class OpenSubtitlesFile
{
    public long? FileId { get; set; }

    public int? CdNumber { get; set; }

    public string? FileName { get; set; }
}

internal sealed class OpenSubtitlesLoginResponse
{
    public string? Token { get; set; }

    public string? BaseUrl { get; set; }

    public OpenSubtitlesLoginUser? User { get; set; }
}

internal sealed class OpenSubtitlesLoginUser
{
    public int? AllowedDownloads { get; set; }
}

internal sealed class OpenSubtitlesDownloadResponse
{
    public string? Link { get; set; }

    public string? FileName { get; set; }
}

internal sealed class OpenSubtitlesMessageResponse
{
    public string? Message { get; set; }
}
