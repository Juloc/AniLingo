using System.Text.Json;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.DownloadClients;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Acquisition.Wanted;

namespace Jularr.Web.Features.Books;

/// <summary>
/// What the Books add dialog stores with a request so it can be executed later. The Usenet
/// search state (tried releases, searches, next search, last problem) is the shared
/// <see cref="ReleaseRequestPayload"/>.
/// </summary>
public sealed record BookRequestPayload(
    string CatalogId,
    string Title,
    string? Author) : ReleaseRequestPayload;

/// <summary>
/// Automatic Books acquisition, Readarr-style but on the same Usenet path as every other media
/// type: a free catalog edition is imported directly; otherwise every indexer is searched in the
/// Newznab Books categories, the best EPUB (or else PDF) release goes to the download client with
/// the Books category, and the shared completed-download dispatcher imports it (see
/// <see cref="BookCompletedDownloadImportAdapter"/>).
/// </summary>
public sealed class BookAcquisitionExecutor(
    BookCatalogService books,
    IndexerSearchCoordinator indexers,
    DownloadClientStore downloadClients,
    DownloadClientSubmissionService downloads,
    ReleaseRequestTracker tracker) : IAcquisitionRequestExecutor
{
    /// <summary>Operation kind of a request-backed Books download.</summary>
    public const string OperationKind = "book-usenet-download";

    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Book;

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

        var search = await BookUsenetSearch.SearchAsync(indexers, payload.Title, payload.Author, cancellationToken);
        return await tracker.ContinueAsync(
            request,
            payload,
            Candidates(search),
            search.FailureMessage,
            async release =>
            {
                var outcome = await downloads.SubmitAsync(
                    new DownloadSubmissionSpec(
                        OperationKind,
                        "Download Book",
                        payload.Title,
                        request.RequestedByProfileId,
                        release.DownloadUri,
                        payload.Title,
                        MediaAcquisitionKind.Book),
                    cancellationToken);
                return new ReleaseRequestSubmission(outcome.Accepted, outcome.OperationId, outcome.Message);
            },
            cancellationToken);
    }

    /// <summary>
    /// The releases the book selector accepted, best first. A Books release is remembered by its
    /// title, as it always was, so requests created before the shared state keep their history.
    /// </summary>
    public static IReadOnlyList<ReleaseRequestCandidate> Candidates(BookUsenetSearchResult search) =>
        search.Ranked
            .Where(candidate => candidate.Score > 0 && candidate.Release.InternalDownloadUri is not null)
            .Select(candidate => new ReleaseRequestCandidate(
                candidate.Release.Title,
                candidate.Release.Title,
                candidate.Release.InternalDownloadUri!))
            .ToArray();

    public static BookRequestPayload ReadPayload(AcquisitionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            try
            {
                if (JsonSerializer.Deserialize<BookRequestPayload>(request.PayloadJson, JsonSerializerOptions.Web) is { } persisted
                    && !string.IsNullOrWhiteSpace(persisted.Title))
                {
                    return persisted;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new BookRequestPayload(request.ExternalId, request.Title, request.Subtitle);
    }
}

/// <summary>Books requests on the shared release-request Wanted policy.</summary>
public sealed class BookWantedRequestHandler(
    AcquisitionAccessStore store,
    AcquisitionRequestService requests) : ReleaseRequestWantedHandler(store, requests)
{
    public override MediaAcquisitionKind Kind => MediaAcquisitionKind.Book;

    protected override ReleaseRequestPayload ReadPayload(AcquisitionRequest request) =>
        BookAcquisitionExecutor.ReadPayload(request);
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
