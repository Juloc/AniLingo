using System.Globalization;
using Jularr.Web.Features.Providers;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// OpenSubtitles.com as an <see cref="ISubtitleProvider"/> (#560), bound to one snapshot of the
/// owner's <see cref="OpenSubtitlesCredential"/>. Search results are cached through the shared
/// <see cref="ProviderResponseCache"/> (served stale if OpenSubtitles is briefly unreachable);
/// every network call goes through <see cref="OpenSubtitlesClient"/> and therefore the provider
/// framework's executor, rate-limit gate and health tracking.
/// </summary>
/// <remarks>
/// Language, forced and SDH follow the searched language-profile item exactly: the query asks
/// OpenSubtitles for that combination only, and every result is labelled with the requested
/// language tag (not OpenSubtitles' regional code), so importing a result satisfies the very
/// profile item that was searched (see <see cref="SubtitleCompletenessService"/>).
/// </remarks>
public sealed class OpenSubtitlesSubtitleProvider(
    OpenSubtitlesCredential credential,
    OpenSubtitlesClient client,
    ProviderResponseCache cache) : ISubtitleProvider, IExternalProvider
{
    public static readonly ExternalProviderDescriptor ProviderDescriptor =
        new(ProviderKeys.OpenSubtitles, "OpenSubtitles", ProviderCapabilities.Subtitles);

    /// <summary>How long an identical search is answered from memory.</summary>
    public static readonly TimeSpan SearchFreshFor = TimeSpan.FromMinutes(15);

    private const int MaxResults = 50;

    public string Id => ProviderKeys.OpenSubtitles;

    public string DisplayName => ProviderDescriptor.DisplayName;

    public ExternalProviderDescriptor Descriptor => ProviderDescriptor;

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codes = ProviderLanguageCodes(request.LanguageTag);
        if (codes.Count == 0 || string.IsNullOrWhiteSpace(request.AnimeTitle))
        {
            return [];
        }

        var parameters = BuildQuery(request, codes);
        var languageTag = NormalizeTag(request.LanguageTag);
        var cacheKey =
            $"{ProviderKeys.OpenSubtitles}:search:{languageTag}:{string.Join('&', parameters.Select(p => $"{p.Key}={p.Value}"))}";

        return await cache.GetOrFetchAsync<IReadOnlyList<SubtitleSearchResult>>(
            cacheKey,
            SearchFreshFor,
            async token => Map(
                await client.SearchAsync(credential, parameters, token),
                request,
                languageTag,
                codes),
            cancellationToken);
    }

    public async Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!long.TryParse(result.ResultToken, NumberStyles.None, CultureInfo.InvariantCulture, out var fileId) ||
            fileId <= 0)
        {
            return new SubtitleDownloadResult(false, null, null, "That OpenSubtitles result is not valid.");
        }

        var ticket = await client.RequestDownloadAsync(credential, fileId, cancellationToken);
        var bytes = await client.FetchFileAsync(ticket.Link, cancellationToken);
        var content = SubtitleDownloadContent.Decode(bytes);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new SubtitleDownloadResult(false, null, null, "OpenSubtitles returned an empty subtitle file.");
        }

        return new SubtitleDownloadResult(true, FormatOf(ticket.FileName), content, null);
    }

    private static List<KeyValuePair<string, string>> BuildQuery(
        SubtitleSearchRequest request,
        IReadOnlyList<string> codes)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("foreign_parts_only", request.Forced ? "only" : "exclude"),
            new("hearing_impaired", request.Sdh ? "only" : "exclude"),
            new("languages", string.Join(',', codes)),
            new("query", request.AnimeTitle.Trim().ToLowerInvariant()),
            new("type", "episode")
        };

        if (request.SeasonNumber > 0)
        {
            parameters.Add(new("season_number", request.SeasonNumber.ToString(CultureInfo.InvariantCulture)));
        }

        if (request.EpisodeNumber > 0)
        {
            parameters.Add(new("episode_number", request.EpisodeNumber.ToString(CultureInfo.InvariantCulture)));
        }

        return parameters;
    }

    private static IReadOnlyList<SubtitleSearchResult> Map(
        IReadOnlyList<OpenSubtitlesSearchHit> hits,
        SubtitleSearchRequest request,
        string languageTag,
        IReadOnlyList<string> codes)
    {
        var title = request.AnimeTitle.Trim();

        return hits
            // Never label a track with a language, forced or SDH flag it does not have: a wrong
            // label would satisfy (or hide) a language-profile item it does not belong to.
            .Where(hit => codes.Contains(hit.Language.ToLowerInvariant()))
            .Where(hit => hit.ForeignPartsOnly == request.Forced && hit.HearingImpaired == request.Sdh)
            .Where(hit => IsRequestedEpisode(hit, request))
            .Select(hit => new SubtitleSearchResult(
                ProviderKeys.OpenSubtitles,
                hit.FileId.ToString(CultureInfo.InvariantCulture),
                languageTag,
                hit.ForeignPartsOnly,
                hit.HearingImpaired,
                hit.Release,
                hit.Uploader,
                Score(hit, title),
                hit.UploadedAt))
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.PublishedAt)
            .Take(MaxResults)
            .ToArray();
    }

    private static bool IsRequestedEpisode(OpenSubtitlesSearchHit hit, SubtitleSearchRequest request) =>
        (hit.EpisodeNumber is not > 0 || request.EpisodeNumber <= 0 || hit.EpisodeNumber == request.EpisodeNumber) &&
        (hit.SeasonNumber is not > 0 || request.SeasonNumber <= 0 || hit.SeasonNumber == request.SeasonNumber);

    // 0..1, higher is better: trusted uploaders and well-downloaded, well-rated files rank first;
    // machine translations rank last; a matching series title breaks ties between look-alike shows.
    private static double Score(OpenSubtitlesSearchHit hit, string requestedTitle)
    {
        var score = 0.5;
        if (hit.FromTrusted)
        {
            score += 0.2;
        }

        score += 0.15 * Math.Min(1.0, Math.Log10(1 + Math.Max(0, hit.DownloadCount)) / 4.0);

        if (hit.Votes > 0)
        {
            score += 0.05 * Math.Clamp(hit.Ratings / 10.0, 0.0, 1.0);
        }

        if (hit.AiTranslated || hit.MachineTranslated)
        {
            score -= 0.3;
        }

        if (hit.Title is { Length: > 0 } hitTitle &&
            requestedTitle.Length > 0 &&
            hitTitle.Contains(requestedTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.1;
        }

        return Math.Round(Math.Clamp(score, 0.0, 1.0), 3);
    }

    private static string FormatOf(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant();
        return extension is "srt" or "ass" or "ssa" or "vtt" ? extension : "srt";
    }

    private static string NormalizeTag(string languageTag) =>
        languageTag.Trim().Replace('_', '-').ToLowerInvariant();

    // Translates a profile language tag ("en", "eng", "pt-BR") into OpenSubtitles' codes: lower-case
    // ISO 639-1, with the regional variants it distinguishes for Portuguese and Chinese.
    private static IReadOnlyList<string> ProviderLanguageCodes(string languageTag)
    {
        var normalized = NormalizeTag(languageTag);
        switch (normalized)
        {
            case "pt-br":
                return ["pt-br"];
            case "pt-pt":
                return ["pt-pt"];
            case "zh-cn" or "zh-hans":
                return ["zh-cn"];
            case "zh-tw" or "zh-hant":
                return ["zh-tw"];
        }

        var primary = normalized.Split('-')[0];
        if (primary.Length is < 2 or > 3 || !primary.All(char.IsAsciiLetter))
        {
            return [];
        }

        return (SubtitleLanguageAliases.CanonicalTagFor(primary) ?? primary) switch
        {
            "pt" => ["pt-pt", "pt-br"],
            "zh" => ["zh-cn", "zh-tw"],
            var canonical => [canonical.ToLowerInvariant()]
        };
    }
}
