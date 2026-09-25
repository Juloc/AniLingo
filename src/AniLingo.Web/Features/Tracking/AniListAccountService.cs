using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Features.MediaMapping;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Tracking;

public sealed class AniListAccountException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record AniListViewer(
    int Id,
    string Name,
    string? AvatarUrl);

public sealed record AniListAccountStatus(
    bool IsConnected,
    int? ClientId,
    int? ViewerId,
    string? ViewerName,
    string? ViewerAvatarUrl,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? TokenExpiresAt)
{
    public static AniListAccountStatus Disconnected { get; } =
        new(false, null, null, null, null, null, null);

    public bool IsExpired =>
        TokenExpiresAt is not null &&
        TokenExpiresAt <= DateTimeOffset.UtcNow;
}

public sealed record AniListFuzzyDate(
    int? Year,
    int? Month,
    int? Day);

public sealed record AniListRemoteListEntry(
    int Id,
    int UserId,
    int MediaId,
    string? Status,
    int Progress,
    double? Score,
    int Repeat,
    int Priority,
    bool Private,
    string? Notes,
    bool HiddenFromStatusLists,
    JsonNode? CustomLists,
    JsonNode? AdvancedScores,
    AniListFuzzyDate? StartedAt,
    AniListFuzzyDate? CompletedAt,
    long? UpdatedAt)
{
    public bool ProtectedFieldsEqual(AniListRemoteListEntry other) =>
        string.Equals(Status, other.Status, StringComparison.Ordinal) &&
        Nullable.Equals(Score, other.Score) &&
        Repeat == other.Repeat &&
        Priority == other.Priority &&
        Private == other.Private &&
        string.Equals(Notes, other.Notes, StringComparison.Ordinal) &&
        HiddenFromStatusLists == other.HiddenFromStatusLists &&
        JsonNode.DeepEquals(CustomLists, other.CustomLists) &&
        JsonNode.DeepEquals(AdvancedScores, other.AdvancedScores) &&
        Equals(StartedAt, other.StartedAt) &&
        Equals(CompletedAt, other.CompletedAt);
}

public enum AniListLibraryMediaType
{
    Anime,
    Manga
}

public sealed record AniListLibraryMedia(
    int MediaId,
    string MediaType,
    string? Format,
    string Title,
    string? NativeTitle,
    string? CoverImageUrl,
    string? MediaStatus,
    string? ListStatus,
    int Progress,
    int? TotalProgress,
    int? VolumeCount,
    int? Year,
    long? UpdatedAt,
    IReadOnlyList<string> Genres)
{
    public bool IsNovel =>
        string.Equals(MediaType, "MANGA", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Format, "NOVEL", StringComparison.OrdinalIgnoreCase);
}

public sealed record AniListProgressPreview(
    bool CanSync,
    bool IsNoOp,
    string Message,
    string? MediaTitle,
    int RequestedProgress,
    int? RemoteProgress,
    string? RemoteStatus,
    int? AniListEpisodeCount)
{
    public static AniListProgressPreview Blocked(
        string message,
        int requestedProgress = 0,
        string? mediaTitle = null,
        int? remoteProgress = null,
        string? remoteStatus = null,
        int? aniListEpisodeCount = null) =>
        new(
            false,
            false,
            message,
            mediaTitle,
            requestedProgress,
            remoteProgress,
            remoteStatus,
            aniListEpisodeCount);
}

public sealed record AniListReadingProgressPreview(
    bool CanSync,
    bool IsNoOp,
    string Message,
    string? MediaTitle,
    int RequestedProgress,
    int? RemoteProgress,
    string? RemoteStatus,
    int? AniListChapterCount)
{
    public static AniListReadingProgressPreview Blocked(
        string message,
        int requestedProgress = 0,
        string? mediaTitle = null,
        int? remoteProgress = null,
        string? remoteStatus = null,
        int? aniListChapterCount = null) =>
        new(
            false,
            false,
            message,
            mediaTitle,
            requestedProgress,
            remoteProgress,
            remoteStatus,
            aniListChapterCount);
}

public sealed record AniListProgressSyncResult(
    bool Success,
    bool Changed,
    string Message);

public sealed record AniListProgressBackup(
    string ProfileId,
    DateTimeOffset CapturedAt,
    string ViewerName,
    int RequestedProgress,
    AniListRemoteListEntry RemoteEntry,
    string MediaType = "ANIME");

public sealed class AniListAccountService(
    HttpClient httpClient,
    AniListAccountStore store,
    AppDbContext db,
    AnimeMetadataService metadataService,
    CurrentAccountContext currentAccount,
    ILogger<AniListAccountService> logger)
{
    private const string ViewerQuery = """
        query {
          Viewer {
            id
            name
            avatar { medium }
          }
        }
        """;

    private const string MediaListQuery = """
        query ($userId: Int!, $mediaId: Int!) {
          MediaList(userId: $userId, mediaId: $mediaId, type: ANIME) {
            id
            userId
            mediaId
            status
            progress
            score
            repeat
            priority
            private
            notes
            hiddenFromStatusLists
            customLists
            advancedScores
            startedAt { year month day }
            completedAt { year month day }
            updatedAt
          }
        }
        """;

    private const string MangaListQuery = """
        query ($userId: Int!, $mediaId: Int!) {
          MediaList(userId: $userId, mediaId: $mediaId, type: MANGA) {
            id
            userId
            mediaId
            status
            progress
            score
            repeat
            priority
            private
            notes
            hiddenFromStatusLists
            customLists
            advancedScores
            startedAt { year month day }
            completedAt { year month day }
            updatedAt
          }
        }
        """;

    private const string MangaMetadataQuery = """
        query ($id: Int!) {
          Media(id: $id, type: MANGA) {
            id
            chapters
          }
        }
        """;

    private const string PersonalLibraryQuery = """
        query ($userId: Int!, $type: MediaType!) {
          MediaListCollection(userId: $userId, type: $type) {
            lists {
              status
              entries {
                id
                status
                progress
                repeat
                updatedAt
                media {
                  id
                  type
                  format
                  title { romaji english native }
                  coverImage { extraLarge large }
                  status
                  episodes
                  chapters
                  volumes
                  seasonYear
                  startDate { year }
                  genres
                  isAdult
                }
              }
            }
          }
        }
        """;

    // Safety invariant: the mutation has exactly two variables and only one mutable
    // list field: progress. Do not add status, score, notes, dates or list settings.
    private const string SaveProgressMutation = """
        mutation ($id: Int!, $progress: Int!) {
          SaveMediaListEntry(id: $id, progress: $progress) {
            id
            userId
            mediaId
            status
            progress
            score
            repeat
            priority
            private
            notes
            hiddenFromStatusLists
            customLists
            advancedScores
            startedAt { year month day }
            completedAt { year month day }
            updatedAt
          }
        }
        """;

    public static string BuildAuthorizationUrl(int clientId)
    {
        if (clientId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientId));
        }

        return $"https://anilist.co/api/v2/oauth/authorize?client_id={clientId}&response_type=token";
    }

    public async Task<AniListAccountStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var stored = await store.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);
        return stored is null
            ? AniListAccountStatus.Disconnected
            : ToStatus(stored);
    }

    public async Task<AniListAccountStatus> ConnectAsync(
        int clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (clientId <= 0)
        {
            throw new AniListAccountException("Enter a valid AniList client ID.");
        }

        var token = accessToken.Trim();
        if (token.Length < 20)
        {
            throw new AniListAccountException("The AniList access token is not valid.");
        }

        var viewer = await FetchViewerAsync(token, cancellationToken);
        var account = new StoredAniListAccount(
            clientId,
            viewer.Id,
            viewer.Name,
            viewer.AvatarUrl,
            token,
            DateTimeOffset.UtcNow,
            TryReadTokenExpiry(token));

        await store.SaveAsync(
            currentAccount.ProfileId,
            account,
            cancellationToken);
        return ToStatus(account);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        store.DisconnectAsync(currentAccount.ProfileId, cancellationToken);

    public async Task<IReadOnlyList<AniListLibraryMedia>> GetLibraryAsync(
        AniListLibraryMediaType mediaType,
        CancellationToken cancellationToken)
    {
        var account = await store.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);
        if (account is null)
        {
            return [];
        }

        if (account.TokenExpiresAt is not null &&
            account.TokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new AniListAccountException(
                "Your AniList connection has expired. Reconnect it in Settings.");
        }

        var body = await SendAuthenticatedAsync(
            account.AccessToken,
            PersonalLibraryQuery,
            new
            {
                userId = account.ViewerId,
                type = mediaType == AniListLibraryMediaType.Anime
                    ? "ANIME"
                    : "MANGA"
            },
            "reading your AniList library",
            cancellationToken);

        return ParseLibraryResponse(body);
    }

    public async Task<AniListReadingProgressPreview> GetMangaProgressPreviewAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await BuildMangaProgressContextAsync(
                seriesId,
                cancellationToken);
            return context.Preview;
        }
        catch (AniListAccountException exception)
        {
            return AniListReadingProgressPreview.Blocked(exception.Message);
        }
    }

    public async Task<AniListProgressSyncResult> SyncMangaProgressAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await BuildMangaProgressContextAsync(
                seriesId,
                cancellationToken);
            if (!context.Preview.CanSync)
            {
                return new AniListProgressSyncResult(
                    Success: context.Preview.IsNoOp,
                    Changed: false,
                    context.Preview.Message);
            }

            var remote = context.RemoteEntry
                ?? throw new AniListAccountException("AniList list entry is missing.");
            var account = context.Account
                ?? throw new AniListAccountException("AniList account context is missing.");

            await store.AppendProgressBackupAsync(
                new AniListProgressBackup(
                    currentAccount.ProfileId,
                    DateTimeOffset.UtcNow,
                    account.ViewerName,
                    context.RequestedProgress,
                    remote,
                    "MANGA"),
                cancellationToken);

            var updated = await SaveProgressAsync(
                account.AccessToken,
                remote.Id,
                context.RequestedProgress,
                cancellationToken);

            ValidateProgressOnlyUpdate(
                remote,
                updated,
                context.RequestedProgress);

            return new AniListProgressSyncResult(
                Success: true,
                Changed: true,
                $"AniList manga chapter progress updated from {remote.Progress} to {updated.Progress}. No other list fields were sent.");
        }
        catch (AniListAccountException exception)
        {
            return new AniListProgressSyncResult(
                Success: false,
                Changed: false,
                exception.Message);
        }
    }

    public async Task<AniListReadingProgressPreview> GetNovelProgressPreviewAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await BuildNovelProgressContextAsync(
                workId,
                cancellationToken);
            return context.Preview;
        }
        catch (AniListAccountException exception)
        {
            return AniListReadingProgressPreview.Blocked(exception.Message);
        }
    }

    public async Task<AniListProgressSyncResult> SyncNovelProgressAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await BuildNovelProgressContextAsync(
                workId,
                cancellationToken);
            if (!context.Preview.CanSync)
            {
                return new AniListProgressSyncResult(
                    Success: context.Preview.IsNoOp,
                    Changed: false,
                    context.Preview.Message);
            }

            var remote = context.RemoteEntry
                ?? throw new AniListAccountException("AniList list entry is missing.");
            var account = context.Account
                ?? throw new AniListAccountException("AniList account context is missing.");

            await store.AppendProgressBackupAsync(
                new AniListProgressBackup(
                    currentAccount.ProfileId,
                    DateTimeOffset.UtcNow,
                    account.ViewerName,
                    context.RequestedProgress,
                    remote,
                    "MANGA"),
                cancellationToken);

            var updated = await SaveProgressAsync(
                account.AccessToken,
                remote.Id,
                context.RequestedProgress,
                cancellationToken);

            ValidateProgressOnlyUpdate(
                remote,
                updated,
                context.RequestedProgress);

            return new AniListProgressSyncResult(
                Success: true,
                Changed: true,
                $"AniList chapter progress updated from {remote.Progress} to {updated.Progress}. No other list fields were sent.");
        }
        catch (AniListAccountException exception)
        {
            return new AniListProgressSyncResult(
                Success: false,
                Changed: false,
                exception.Message);
        }
    }

    public async Task<AniListProgressPreview> GetEpisodeProgressPreviewAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await BuildProgressContextAsync(episodeId, cancellationToken);
            return context.Preview;
        }
        catch (AniListAccountException exception)
        {
            return AniListProgressPreview.Blocked(exception.Message);
        }
    }

    public async Task<AniListProgressSyncResult> SyncEpisodeProgressAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Read immediately before every write. Never trust a stale page preview.
            var context = await BuildProgressContextAsync(episodeId, cancellationToken);
            if (!context.Preview.CanSync)
            {
                return new AniListProgressSyncResult(
                    Success: context.Preview.IsNoOp,
                    Changed: false,
                    context.Preview.Message);
            }

            var remote = context.RemoteEntry
                ?? throw new AniListAccountException("AniList list entry is missing.");

            var account = context.Account
                ?? throw new AniListAccountException("AniList account context is missing.");

            await store.AppendProgressBackupAsync(
                new AniListProgressBackup(
                    currentAccount.ProfileId,
                    DateTimeOffset.UtcNow,
                    account.ViewerName,
                    context.RequestedProgress,
                    remote),
                cancellationToken);

            var updated = await SaveProgressAsync(
                account.AccessToken,
                remote.Id,
                context.RequestedProgress,
                cancellationToken);

            ValidateProgressOnlyUpdate(
                remote,
                updated,
                context.RequestedProgress);

            return new AniListProgressSyncResult(
                Success: true,
                Changed: true,
                $"AniList progress updated from {remote.Progress} to {updated.Progress}. No other list fields were sent.");
        }
        catch (AniListAccountException exception)
        {
            return new AniListProgressSyncResult(
                Success: false,
                Changed: false,
                exception.Message);
        }
    }

    private async Task<ReadingProgressContext> BuildMangaProgressContextAsync(
        Guid seriesId,
        CancellationToken cancellationToken)
    {
        var repository = new MangaRepository(db);
        var local = await repository.GetAniListProgressContextAsync(
            currentAccount.ProfileId,
            seriesId,
            cancellationToken);

        if (local is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Read part of the manga in AniLingo before syncing progress."));
        }

        if (!int.TryParse(local.MetadataExternalId, out var mediaId) ||
            mediaId <= 0)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Match this manga to AniList before syncing progress.",
                    mediaTitle: local.Title));
        }

        var resolved = AutomaticMediaMatcher.ResolveReadingProgress(
            local.ChapterNumber,
            local.PageIndex,
            Math.Max(0, local.PageCount - 1));

        if (!resolved.CanSync)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    resolved.Reason ?? "The local manga progress cannot be mapped safely.",
                    resolved.Progress,
                    local.Title));
        }

        var account = await store.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);
        if (account is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Connect your AniList account in Settings before syncing progress.",
                    resolved.Progress,
                    local.Title));
        }

        if (account.TokenExpiresAt is not null &&
            account.TokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Your AniList connection has expired. Reconnect it in Settings.",
                    resolved.Progress,
                    local.Title));
        }

        var remote = await FetchMangaListEntryAsync(
            account,
            mediaId,
            cancellationToken);
        if (remote is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "This manga is not on your AniList list. Add it in AniList first.",
                    resolved.Progress,
                    local.Title));
        }

        var chapterCount = await FetchMangaChapterCountAsync(
            account,
            mediaId,
            cancellationToken);
        var preview = EvaluateRemoteChapterProgressSafety(
            remote,
            resolved.Progress,
            chapterCount,
            local.Title,
            "manga");

        return new ReadingProgressContext(
            account,
            remote,
            resolved.Progress,
            preview);
    }

    private async Task<ReadingProgressContext> BuildNovelProgressContextAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => new
            {
                Title = x.MetadataTitle ?? x.Title,
                x.MetadataProvider,
                x.MetadataExternalId,
                x.MetadataChapterCount
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            throw new AniListAccountException("Novel work was not found.");
        }

        if (!string.Equals(
                work.MetadataProvider,
                "anilist",
                StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(work.MetadataExternalId, out var mediaId) ||
            mediaId <= 0)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Match this light novel to AniList before syncing progress.",
                    mediaTitle: work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var localProgress = await db.NovelProgress
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProfileId == currentAccount.ProfileId &&
                    x.WorkId == workId,
                cancellationToken);

        if (localProgress is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Read part of the novel in AniLingo before syncing progress.",
                    mediaTitle: work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var chapterNumber = await db.NovelChapters
            .AsNoTracking()
            .Where(x =>
                x.Id == localProgress.ChapterId &&
                x.WorkId == workId)
            .Select(x => (int?)x.Number)
            .SingleOrDefaultAsync(cancellationToken);

        if (chapterNumber is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "The current local reading chapter could not be resolved.",
                    mediaTitle: work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var requestedProgress = localProgress.PositionPermille >= 950
            ? chapterNumber.Value
            : Math.Max(0, chapterNumber.Value - 1);

        if (requestedProgress <= 0)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Finish the first chapter before syncing AniList chapter progress.",
                    requestedProgress,
                    work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var account = await store.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);
        if (account is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Connect your AniList account in Settings before syncing progress.",
                    requestedProgress,
                    work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        if (account.TokenExpiresAt is not null &&
            account.TokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "Your AniList connection has expired. Reconnect it in Settings.",
                    requestedProgress,
                    work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var remote = await FetchMangaListEntryAsync(
            account,
            mediaId,
            cancellationToken);
        if (remote is null)
        {
            return ReadingProgressContext.Blocked(
                AniListReadingProgressPreview.Blocked(
                    "This light novel is not on your AniList list. Add it in AniList first.",
                    requestedProgress,
                    work.Title,
                    aniListChapterCount: work.MetadataChapterCount));
        }

        var preview = EvaluateRemoteChapterProgressSafety(
            remote,
            requestedProgress,
            work.MetadataChapterCount,
            work.Title);

        return new ReadingProgressContext(
            account,
            remote,
            requestedProgress,
            preview);
    }

    private async Task<ProgressContext> BuildProgressContextAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episode = await (
            from localEpisode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on localEpisode.AnimeId equals anime.Id
            where localEpisode.Id == episodeId
            select new
            {
                localEpisode.AnimeId,
                localEpisode.Number,
                localEpisode.SeasonNumber,
                AnimeTitle = anime.Title
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (episode is null)
        {
            throw new AniListAccountException("Local episode was not found.");
        }

        if (episode.Number <= 0)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    "Special/unnumbered episodes are not synced automatically.",
                    episode.Number,
                    episode.AnimeTitle));
        }

        ResolvedAnimeEpisodeMetadata? resolved;
        try
        {
            resolved = await metadataService.ResolveEpisodeAsync(
                episodeId,
                cancellationToken);
        }
        catch (AniListAccountException exception)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    exception.Message,
                    episode.Number,
                    episode.AnimeTitle));
        }

        if (resolved is null ||
            !string.Equals(
                resolved.Provider,
                AniListMetadataProvider.ProviderKey,
                StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(resolved.ExternalId, out var mediaId) ||
            mediaId <= 0)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    $"No AniList episode mapping exists for S{episode.SeasonNumber:00}E{episode.Number:00}. Add a range mapping on the anime page before syncing progress.",
                    episode.Number,
                    episode.AnimeTitle));
        }

        if (resolved.EpisodeCount is > 0 &&
            resolved.RemoteEpisodeNumber > resolved.EpisodeCount.Value)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    $"Mapped AniList episode {resolved.RemoteEpisodeNumber} is above the known episode count ({resolved.EpisodeCount}). Sync blocked.",
                    resolved.RemoteEpisodeNumber,
                    resolved.PreferredTitle,
                    aniListEpisodeCount: resolved.EpisodeCount));
        }

        var account = await store.LoadAsync(
            currentAccount.ProfileId,
            cancellationToken);
        if (account is null)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    "Connect your AniList account in Settings before syncing progress.",
                    resolved.RemoteEpisodeNumber,
                    resolved.PreferredTitle,
                    aniListEpisodeCount: resolved.EpisodeCount));
        }

        if (account.TokenExpiresAt is not null &&
            account.TokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    "Your AniList connection has expired. Reconnect it in Settings.",
                    resolved.RemoteEpisodeNumber,
                    resolved.PreferredTitle,
                    aniListEpisodeCount: resolved.EpisodeCount));
        }

        var remote = await FetchListEntryAsync(
            account,
            mediaId,
            cancellationToken);

        if (remote is null)
        {
            return ProgressContext.Blocked(
                AniListProgressPreview.Blocked(
                    "This anime is not on your AniList list. AniLingo will not create a list entry automatically.",
                    resolved.RemoteEpisodeNumber,
                    resolved.PreferredTitle,
                    aniListEpisodeCount: resolved.EpisodeCount));
        }

        var remoteSafety = EvaluateRemoteProgressSafety(
            remote,
            resolved.RemoteEpisodeNumber,
            resolved.EpisodeCount,
            resolved.PreferredTitle);

        return new ProgressContext(
            account,
            remote,
            resolved.RemoteEpisodeNumber,
            remoteSafety);
    }

    public static AniListProgressPreview EvaluateRemoteProgressSafety(
        AniListRemoteListEntry remote,
        int requestedProgress,
        int? aniListEpisodeCount,
        string? mediaTitle)
    {
        if (requestedProgress <= 0)
        {
            return AniListProgressPreview.Blocked(
                "Special/unnumbered episodes are not synced automatically.",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListEpisodeCount);
        }

        if (remote.Progress >= requestedProgress)
        {
            return new AniListProgressPreview(
                CanSync: false,
                IsNoOp: true,
                $"AniList already has progress {remote.Progress}; AniLingo never lowers progress.",
                mediaTitle,
                requestedProgress,
                remote.Progress,
                remote.Status,
                aniListEpisodeCount);
        }

        if (!string.Equals(remote.Status, "CURRENT", StringComparison.OrdinalIgnoreCase))
        {
            return AniListProgressPreview.Blocked(
                $"AniList status is {remote.Status ?? "unknown"}. For safety, AniLingo only writes progress while the entry is CURRENT (Watching). Change the status in AniList first.",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListEpisodeCount);
        }

        if (aniListEpisodeCount is > 0 &&
            requestedProgress >= aniListEpisodeCount.Value)
        {
            return AniListProgressPreview.Blocked(
                "This is the final AniList episode. AniLingo does not sync the last episode automatically because AniList may also change completion status/date. Finish the entry in AniList itself.",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListEpisodeCount);
        }

        return new AniListProgressPreview(
            CanSync: true,
            IsNoOp: false,
            $"Ready to increase AniList progress from {remote.Progress} to {requestedProgress}.",
            mediaTitle,
            requestedProgress,
            remote.Progress,
            remote.Status,
            aniListEpisodeCount);
    }

    public static AniListReadingProgressPreview EvaluateRemoteChapterProgressSafety(
        AniListRemoteListEntry remote,
        int requestedProgress,
        int? aniListChapterCount,
        string? mediaTitle,
        string mediaKind = "light novel")
    {
        if (remote.Progress >= requestedProgress)
        {
            return new AniListReadingProgressPreview(
                CanSync: false,
                IsNoOp: true,
                $"AniList already has chapter progress {remote.Progress}; AniLingo never lowers progress.",
                mediaTitle,
                requestedProgress,
                remote.Progress,
                remote.Status,
                aniListChapterCount);
        }

        if (aniListChapterCount is not > 0)
        {
            return AniListReadingProgressPreview.Blocked(
                $"AniList does not expose a reliable chapter count for this {mediaKind}, so AniLingo cannot safely assume the local chapter numbering matches.",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListChapterCount);
        }

        if (!string.Equals(remote.Status, "CURRENT", StringComparison.OrdinalIgnoreCase))
        {
            return AniListReadingProgressPreview.Blocked(
                $"AniList status is {remote.Status ?? "unknown"}. For safety, AniLingo only writes chapter progress while the entry is CURRENT (Reading).",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListChapterCount);
        }

        if (aniListChapterCount is > 0 &&
            requestedProgress >= aniListChapterCount.Value)
        {
            return AniListReadingProgressPreview.Blocked(
                "This would reach the final AniList chapter. AniLingo leaves completion status/date to AniList.",
                requestedProgress,
                mediaTitle,
                remote.Progress,
                remote.Status,
                aniListChapterCount);
        }

        return new AniListReadingProgressPreview(
            CanSync: true,
            IsNoOp: false,
            $"Ready to increase AniList chapter progress from {remote.Progress} to {requestedProgress}.",
            mediaTitle,
            requestedProgress,
            remote.Progress,
            remote.Status,
            aniListChapterCount);
    }

    private async Task<AniListViewer> FetchViewerAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var body = await SendAuthenticatedAsync(
            accessToken,
            ViewerQuery,
            new { },
            "validating the account",
            cancellationToken);

        return ParseViewerResponse(body);
    }

    private async Task<AniListRemoteListEntry?> FetchListEntryAsync(
        StoredAniListAccount account,
        int mediaId,
        CancellationToken cancellationToken)
    {
        var body = await SendAuthenticatedAsync(
            account.AccessToken,
            MediaListQuery,
            new
            {
                userId = account.ViewerId,
                mediaId
            },
            "reading list progress",
            cancellationToken);

        return ParseListEntryResponse(body, "MediaList");
    }

    private async Task<AniListRemoteListEntry?> FetchMangaListEntryAsync(
        StoredAniListAccount account,
        int mediaId,
        CancellationToken cancellationToken)
    {
        var body = await SendAuthenticatedAsync(
            account.AccessToken,
            MangaListQuery,
            new
            {
                userId = account.ViewerId,
                mediaId
            },
            "reading manga/novel progress",
            cancellationToken);

        return ParseListEntryResponse(body, "MediaList");
    }

    private async Task<int?> FetchMangaChapterCountAsync(
        StoredAniListAccount account,
        int mediaId,
        CancellationToken cancellationToken)
    {
        var body = await SendAuthenticatedAsync(
            account.AccessToken,
            MangaMetadataQuery,
            new { id = mediaId },
            "reading manga metadata",
            cancellationToken);

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Media", out var media) ||
            media.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadInt(media, "chapters");
    }

    private async Task<AniListRemoteListEntry> SaveProgressAsync(
        string accessToken,
        int listEntryId,
        int progress,
        CancellationToken cancellationToken)
    {
        var body = await SendAuthenticatedAsync(
            accessToken,
            SaveProgressMutation,
            BuildProgressMutationVariables(listEntryId, progress),
            "saving list progress",
            cancellationToken);

        return ParseListEntryResponse(body, "SaveMediaListEntry")
            ?? throw new AniListAccountException(
                "AniList returned no list entry after saving progress.");
    }

    private void ValidateProgressOnlyUpdate(
        AniListRemoteListEntry remote,
        AniListRemoteListEntry updated,
        int requestedProgress)
    {
        if (updated.Id != remote.Id ||
            updated.UserId != remote.UserId ||
            updated.MediaId != remote.MediaId ||
            updated.Progress != requestedProgress)
        {
            logger.LogCritical(
                "AniList returned an unexpected list entry after progress sync. Entry {EntryId}, media {MediaId}.",
                remote.Id,
                remote.MediaId);
            throw new AniListAccountException(
                "AniList returned an unexpected progress response. No further sync was attempted.");
        }

        if (!remote.ProtectedFieldsEqual(updated))
        {
            logger.LogCritical(
                "AniList protected list fields changed unexpectedly while updating progress-only for entry {EntryId}. A pre-write backup was saved under /data/integrations.",
                remote.Id);
            throw new AniListAccountException(
                "AniList changed fields outside progress unexpectedly. Sync stopped and a pre-write backup was saved.");
        }
    }

    public static IReadOnlyDictionary<string, int> BuildProgressMutationVariables(
        int listEntryId,
        int progress)
    {
        if (listEntryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listEntryId));
        }

        if (progress < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["id"] = listEntryId,
            ["progress"] = progress
        };
    }

    private async Task<string> SendAuthenticatedAsync(
        string accessToken,
        string query,
        object variables,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new
            {
                query,
                variables
            });

            using var response = await httpClient.SendAsync(
                request,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new AniListAccountException(
                    $"AniList returned HTTP {(int)response.StatusCode} while {operation}.");
            }

            ThrowIfGraphQlErrors(body);
            return body;
        }
        catch (AniListAccountException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException)
        {
            logger.LogWarning(exception, "AniList request failed while {Operation}.", operation);
            throw new AniListAccountException(
                $"AniList is currently unavailable while {operation}.",
                exception);
        }
    }

    public static IReadOnlyList<AniListLibraryMedia> ParseLibraryResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        ThrowIfGraphQlErrors(document.RootElement);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("MediaListCollection", out var collection) ||
            collection.ValueKind != JsonValueKind.Object ||
            !collection.TryGetProperty("lists", out var lists) ||
            lists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<AniListLibraryMedia>();
        foreach (var list in lists.EnumerateArray())
        {
            if (!list.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("media", out var media) ||
                    media.ValueKind != JsonValueKind.Object ||
                    ReadBool(media, "isAdult") ||
                    !TryReadInt(media, "id", out var mediaId))
                {
                    continue;
                }

                var type = ReadString(media, "type") ?? "";
                if (type.Length == 0)
                {
                    continue;
                }

                var titleElement = media.TryGetProperty("title", out var title)
                    ? title
                    : default;
                var preferredTitle =
                    ReadString(titleElement, "english") ??
                    ReadString(titleElement, "romaji") ??
                    ReadString(titleElement, "native") ??
                    $"AniList {mediaId}";

                string? cover = null;
                if (media.TryGetProperty("coverImage", out var coverElement) &&
                    coverElement.ValueKind == JsonValueKind.Object)
                {
                    cover =
                        ReadString(coverElement, "extraLarge") ??
                        ReadString(coverElement, "large");
                }

                int? year = ReadInt(media, "seasonYear");
                if (year is null &&
                    media.TryGetProperty("startDate", out var startDate) &&
                    startDate.ValueKind == JsonValueKind.Object)
                {
                    year = ReadInt(startDate, "year");
                }

                var totalProgress = string.Equals(
                        type,
                        "ANIME",
                        StringComparison.OrdinalIgnoreCase)
                    ? ReadInt(media, "episodes")
                    : ReadInt(media, "chapters");

                result.Add(new AniListLibraryMedia(
                    mediaId,
                    type,
                    ReadString(media, "format"),
                    preferredTitle,
                    ReadString(titleElement, "native"),
                    cover,
                    ReadString(media, "status"),
                    ReadString(entry, "status"),
                    ReadInt(entry, "progress") ?? 0,
                    totalProgress,
                    ReadInt(media, "volumes"),
                    year,
                    ReadLong(entry, "updatedAt"),
                    ReadStringArray(media, "genres")));
            }
        }

        return result
            .OrderByDescending(x => x.UpdatedAt ?? 0)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static AniListViewer ParseViewerResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        ThrowIfGraphQlErrors(document.RootElement);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Viewer", out var viewer) ||
            viewer.ValueKind != JsonValueKind.Object ||
            !viewer.TryGetProperty("id", out var idElement) ||
            !idElement.TryGetInt32(out var id) ||
            !viewer.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new AniListAccountException(
                "AniList returned an unexpected account response.");
        }

        var name = nameElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AniListAccountException(
                "AniList returned an account without a username.");
        }

        string? avatarUrl = null;
        if (viewer.TryGetProperty("avatar", out var avatar) &&
            avatar.ValueKind == JsonValueKind.Object &&
            avatar.TryGetProperty("medium", out var medium) &&
            medium.ValueKind == JsonValueKind.String)
        {
            avatarUrl = medium.GetString();
        }

        return new AniListViewer(id, name, avatarUrl);
    }

    public static AniListRemoteListEntry? ParseListEntryResponse(
        string json,
        string fieldName)
    {
        using var document = JsonDocument.Parse(json);
        ThrowIfGraphQlErrors(document.RootElement);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty(fieldName, out var entry) ||
            entry.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (entry.ValueKind != JsonValueKind.Object ||
            !TryReadInt(entry, "id", out var id) ||
            !TryReadInt(entry, "userId", out var userId) ||
            !TryReadInt(entry, "mediaId", out var mediaId))
        {
            throw new AniListAccountException(
                "AniList returned an unexpected list-entry response.");
        }

        return new AniListRemoteListEntry(
            id,
            userId,
            mediaId,
            ReadString(entry, "status"),
            ReadInt(entry, "progress") ?? 0,
            ReadDouble(entry, "score"),
            ReadInt(entry, "repeat") ?? 0,
            ReadInt(entry, "priority") ?? 0,
            ReadBool(entry, "private"),
            ReadString(entry, "notes"),
            ReadBool(entry, "hiddenFromStatusLists"),
            ReadJsonNode(entry, "customLists"),
            ReadJsonNode(entry, "advancedScores"),
            ReadFuzzyDate(entry, "startedAt"),
            ReadFuzzyDate(entry, "completedAt"),
            ReadLong(entry, "updatedAt"));
    }

    public static DateTimeOffset? TryReadTokenExpiry(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(
                payload.Length + ((4 - payload.Length % 4) % 4),
                '=');

            using var document = JsonDocument.Parse(
                Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

            if (!document.RootElement.TryGetProperty("exp", out var expiry) ||
                !expiry.TryGetInt64(out var unixSeconds))
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (Exception exception) when (
            exception is FormatException or
            JsonException or
            ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static void ThrowIfGraphQlErrors(string json)
    {
        using var document = JsonDocument.Parse(json);
        ThrowIfGraphQlErrors(document.RootElement);
    }

    private static void ThrowIfGraphQlErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Array ||
            errors.GetArrayLength() == 0)
        {
            return;
        }

        var message = errors[0].TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;

        throw new AniListAccountException(
            string.IsNullOrWhiteSpace(message)
                ? "AniList returned a GraphQL error."
                : $"AniList: {message}");
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool TryReadInt(
        JsonElement element,
        string propertyName,
        out int value)
    {
        var parsed = ReadInt(element, propertyName);
        value = parsed ?? 0;
        return parsed.HasValue;
    }

    private static long? ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    private static double? ReadDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static JsonNode? ReadJsonNode(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(value.GetRawText());
    }

    private static AniListFuzzyDate? ReadFuzzyDate(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var year = ReadInt(value, "year");
        var month = ReadInt(value, "month");
        var day = ReadInt(value, "day");

        return year is null && month is null && day is null
            ? null
            : new AniListFuzzyDate(year, month, day);
    }

    private static AniListAccountStatus ToStatus(StoredAniListAccount account) =>
        new(
            true,
            account.ClientId,
            account.ViewerId,
            account.ViewerName,
            account.ViewerAvatarUrl,
            account.ConnectedAt,
            account.TokenExpiresAt);

    private sealed record ReadingProgressContext(
        StoredAniListAccount? Account,
        AniListRemoteListEntry? RemoteEntry,
        int RequestedProgress,
        AniListReadingProgressPreview Preview)
    {
        public static ReadingProgressContext Blocked(
            AniListReadingProgressPreview preview) =>
            new(
                null,
                null,
                preview.RequestedProgress,
                preview);
    }

    private sealed record ProgressContext(
        StoredAniListAccount? Account,
        AniListRemoteListEntry? RemoteEntry,
        int RequestedProgress,
        AniListProgressPreview Preview)
    {
        public static ProgressContext Blocked(AniListProgressPreview preview) =>
            new(
                null,
                null,
                preview.RequestedProgress,
                preview);
    }
}
