using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Acquisition.Sabnzbd;

namespace Jularr.Web.Features.Books;

/// <summary>
/// What the Books add dialog stores with a request so it can be executed later, plus the Usenet
/// search state: releases already sent to SABnzbd (never sent twice), when to search again
/// while no release exists yet and why the last release did not work out.
/// </summary>
public sealed record BookRequestPayload(
    string CatalogId,
    string Title,
    string? Author,
    IReadOnlyList<string>? TriedReleases = null,
    int Searches = 0,
    DateTime? NextSearchUtc = null,
    string? LastProblem = null);

/// <summary>
/// Automatic Books acquisition, Readarr-style but on the same Usenet path as anime: a free
/// catalog edition is imported directly; otherwise every indexer is searched in the Newznab
/// Books categories, the best EPUB (or else PDF) release goes to SABnzbd (Books category) and
/// the download import brings it into the library when the download completes.
/// </summary>
public sealed class BookAcquisitionExecutor(
    BookCatalogService books,
    IndexerSearchCoordinator indexers,
    DownloadClientStore downloadClients,
    SabnzbdDownloadService sabnzbd,
    AcquisitionAccessStore requests,
    TimeProvider? clock = null) : IAcquisitionRequestExecutor
{
    /// <summary>After this many searches without a release the request fails and waits for the owner.</summary>
    public const int MaxSearches = 12;

    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Book;

    /// <summary>Wait before the next search while no release exists: 6 h, 12 h, then daily.</summary>
    public static TimeSpan SearchBackoff(int searches) =>
        TimeSpan.FromHours(searches switch { <= 1 => 6, 2 => 12, _ => 24 });

    public async Task<AcquisitionExecution> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken)
    {
        var payload = ReadPayload(request);

        string? freeEditionNote = null;
        // Free first: the catalog edition itself, or a Project Gutenberg twin of the same title
        // (AcquireCatalogBookAsync looks that up). Only without a free edition does it go to Usenet.
        try
        {
            var workId = await books.AcquireCatalogBookAsync(payload.CatalogId, cancellationToken);
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Completed,
                "Imported a free edition.",
                ResultUrl: $"/Books/Library/{workId}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // No authorized free EPUB for this title (or the free catalogs are unreachable).
            freeEditionNote = exception.Message;
        }

        if (!await indexers.HasEnabledIndexerAsync(cancellationToken))
        {
            return new AcquisitionExecution(AcquisitionRequestStatus.Failed, $"No free edition ({freeEditionNote}) and no indexer is configured.");
        }

        var sabnzbdConfigured = (await downloadClients.LoadAllAsync(cancellationToken))
            .Any(entry => entry.Enabled && entry.Type == DownloadClientType.Sabnzbd);
        if (!sabnzbdConfigured)
        {
            return new AcquisitionExecution(AcquisitionRequestStatus.Failed, $"No free edition ({freeEditionNote}) and SABnzbd is not configured.");
        }

        var tried = new HashSet<string>(payload.TriedReleases ?? [], StringComparer.OrdinalIgnoreCase);
        var search = await BookUsenetSearch.SearchAsync(indexers, payload.Title, payload.Author, cancellationToken);
        var searches = payload.Searches + 1;
        var release = search.Ranked
            .Where(candidate => candidate.Score > 0 && !tried.Contains(candidate.Release.Title))
            .Select(candidate => candidate.Release)
            .FirstOrDefault();
        if (release?.InternalDownloadUri is not { } downloadUri)
        {
            var reason = tried.Count > 0 && search.Picked is not null
                ? "Every matching release was tried already."
                : search.FailureMessage;
            if (searches >= MaxSearches)
            {
                await SavePayloadAsync(request, payload with { Searches = searches, NextSearchUtc = null, LastProblem = null }, cancellationToken);
                return new AcquisitionExecution(AcquisitionRequestStatus.Failed, WithProblem(payload, $"{reason} Gave up after {searches} searches."));
            }

            // Readarr-style: keep the request and look again later; new uploads appear all the time.
            var next = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime + SearchBackoff(searches);
            await SavePayloadAsync(request, payload with { Searches = searches, NextSearchUtc = next, LastProblem = null }, cancellationToken);
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Approved,
                WithProblem(payload, $"{reason} Searching again {next:yyyy-MM-dd HH:mm} UTC."));
        }

        await SavePayloadAsync(
            request,
            payload with { TriedReleases = [.. tried, release.Title], Searches = searches, NextSearchUtc = null, LastProblem = null },
            cancellationToken);

        var outcome = await sabnzbd.SubmitUrlAsync(
            new SabnzbdSubmission(
                BookInboxImport.SabnzbdDownloadKind,
                "SABnzbd download",
                payload.Title,
                request.RequestedByProfileId,
                SabnzbdPurpose.Books,
                JobName: payload.Title),
            downloadUri,
            cancellationToken);

        return outcome.Accepted
            ? new AcquisitionExecution(
                AcquisitionRequestStatus.Downloading,
                payload.LastProblem is null ? release.Title : $"{payload.LastProblem} Trying {release.Title}.",
                outcome.OperationId)
            : new AcquisitionExecution(AcquisitionRequestStatus.Failed, outcome.Message);
    }

    /// <summary>Keeps the reason the previous release was dropped visible in the request status.</summary>
    private static string WithProblem(BookRequestPayload payload, string message) =>
        payload.LastProblem is null ? message : $"{payload.LastProblem} {message}";

    private Task SavePayloadAsync(AcquisitionRequest request, BookRequestPayload payload, CancellationToken cancellationToken) =>
        requests.UpdatePayloadAsync(request.Id, JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), cancellationToken);

    public static BookRequestPayload ReadPayload(AcquisitionRequest request) =>
        (string.IsNullOrWhiteSpace(request.PayloadJson)
            ? null
            : JsonSerializer.Deserialize<BookRequestPayload>(request.PayloadJson, JsonSerializerOptions.Web))
        ?? new BookRequestPayload(request.ExternalId, request.Title, request.Subtitle);
}

/// <summary>One indexer result as the book selector judged it; <see cref="Score"/> 0 means rejected.</summary>
public sealed record RankedBookRelease(ProwlarrReleaseCandidate Release, int Score, string? RejectedBecause);

/// <summary>The outcome of one book search on the indexers, shared by automatic adding and the admin test tool.</summary>
public sealed record BookUsenetSearchResult(
    IReadOnlyList<string> Queries,
    IReadOnlyList<RankedBookRelease> Ranked,
    IReadOnlyList<IndexerSearchWarning> Warnings,
    bool UsedCategoryFallback)
{
    public ProwlarrReleaseCandidate? Picked => Ranked.FirstOrDefault(release => release.Score > 0)?.Release;

    public string FailureMessage =>
        Ranked.Count == 0
            ? Warnings.Count > 0
                ? $"No release found on the indexers ({Warnings[0].IndexerName}: {Warnings[0].Message})."
                : "No release found on the indexers."
            : "No suitable EPUB or PDF release found on the indexers.";
}

/// <summary>
/// Searches the indexers for one book: "author title" and "title" in each indexer's Books
/// categories and, when that finds nothing, once more without a category because many
/// indexers file ebooks inconsistently.
/// </summary>
public static class BookUsenetSearch
{
    public static IReadOnlyList<string> Queries(string title, string? author)
    {
        var fullTitle = title.Trim();
        var mainTitle = BookReleaseSelector.MainTitle(fullTitle);
        var queries = new List<string>();
        if (!string.IsNullOrWhiteSpace(author))
        {
            queries.Add($"{author.Trim()} {mainTitle}");
        }

        queries.Add(mainTitle);
        if (!mainTitle.Equals(fullTitle, StringComparison.OrdinalIgnoreCase))
        {
            queries.Add(fullTitle);
        }

        return queries;
    }

    public static async Task<BookUsenetSearchResult> SearchAsync(
        IndexerSearchCoordinator indexers,
        string title,
        string? author,
        CancellationToken cancellationToken)
    {
        var queries = Queries(title, author);
        var result = await indexers.SearchCategoriesAsync(queries, entry => entry.Settings.EffectiveBookCategories, cancellationToken);
        var fallback = false;
        if (result.Releases.Count == 0)
        {
            var anyCategory = await indexers.SearchCategoriesAsync(queries, _ => [], cancellationToken);
            if (anyCategory.Releases.Count > 0)
            {
                result = anyCategory;
                fallback = true;
            }
        }

        return new BookUsenetSearchResult(
            queries,
            BookReleaseSelector.Rank(result.Releases, title, author),
            result.Warnings,
            fallback);
    }
}

/// <summary>Ranks indexer results for one book: Usenet only, EPUB first, then PDF, title words must match.</summary>
public static class BookReleaseSelector
{
    private static readonly string[] UnsupportedFormats = ["mobi", "azw3", "azw", "djvu", "cbr", "cbz", "mp3", "m4b", "audiobook", "hörbuch"];

    public static ProwlarrReleaseCandidate? Pick(
        IReadOnlyList<ProwlarrReleaseCandidate> releases,
        string title,
        string? author) =>
        Rank(releases, title, author).FirstOrDefault(release => release.Score > 0)?.Release;

    /// <summary>Every release, best first; rejected releases (score 0) last, each with its reason.</summary>
    public static IReadOnlyList<RankedBookRelease> Rank(
        IReadOnlyList<ProwlarrReleaseCandidate> releases,
        string title,
        string? author)
    {
        // Subtitles ("Dune: Deluxe Edition") rarely appear in release names; the main title must.
        var titleWords = Words(MainTitle(title));
        var authorWords = Words(author);
        return releases
            .Select(release => Judge(release, titleWords, authorWords))
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Release.PublishedAt)
            .ToArray();
    }

    /// <summary>The title before a subtitle separator (":", ";" or " - ").</summary>
    public static string MainTitle(string title)
    {
        var trimmed = title.Trim();
        var cut = trimmed.IndexOfAny([':', ';']);
        var dash = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && (cut < 0 || dash < cut))
        {
            cut = dash;
        }

        return cut > 0 ? trimmed[..cut].Trim() : trimmed;
    }

    private static RankedBookRelease Judge(
        ProwlarrReleaseCandidate release,
        IReadOnlyCollection<string> titleWords,
        IReadOnlyCollection<string> authorWords)
    {
        if (release.InternalDownloadUri is null)
        {
            return new RankedBookRelease(release, 0, "no download link");
        }

        if (release.Protocol is not null && !release.Protocol.Equals("usenet", StringComparison.OrdinalIgnoreCase))
        {
            return new RankedBookRelease(release, 0, "not a Usenet release");
        }

        if (titleWords.Count == 0)
        {
            return new RankedBookRelease(release, 0, "empty title");
        }

        var words = Words(release.Title);
        var matchedTitle = titleWords.Count(words.Contains);
        // Every significant title word must appear; otherwise it is another book.
        if (matchedTitle < titleWords.Count)
        {
            return new RankedBookRelease(release, 0, "title does not match");
        }

        var formatWords = words.Where(word => UnsupportedFormats.Contains(word)).ToArray();
        var isEpub = words.Contains("epub");
        var isPdf = words.Contains("pdf");
        if (!isEpub && !isPdf && formatWords.Length > 0)
        {
            return new RankedBookRelease(release, 0, $"{formatWords[0].ToUpperInvariant()}, not EPUB or PDF");
        }

        // EPUB is preferred, PDF accepted; a name without a format may still hold either, which
        // the download import checks.
        var score = 10 + matchedTitle;
        score += authorWords.Count(words.Contains) * 2;
        if (isEpub)
        {
            score += 8;
        }
        else if (isPdf)
        {
            score += 3;
        }

        if (words.Contains("retail"))
        {
            score += 2;
        }

        if (release.SizeBytes is > 200L * 1024 * 1024)
        {
            score -= 6;
        }

        return new RankedBookRelease(release, Math.Max(score, 1), null);
    }

    private static HashSet<string> Words(string? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (var word in value.Split(
                     [' ', '.', '_', '-', ':', ',', '(', ')', '[', ']', '\'', '"', '!', '?', '&', '/'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && !StopWords.Contains(word))
            {
                result.Add(word.ToLowerInvariant());
            }
        }

        return result;
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "and", "der", "die", "das", "und", "des", "le", "la", "les"
    };
}
