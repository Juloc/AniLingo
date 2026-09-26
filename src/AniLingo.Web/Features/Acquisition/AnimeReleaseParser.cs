using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Acquisition;

public enum AnimeReleaseSource
{
    Unknown,
    WebDl,
    WebRip,
    BluRay,
    BluRayRip,
    Hdtv
}

public enum AnimeVideoCodec
{
    Unknown,
    Avc,
    Hevc,
    Av1
}

public enum AnimeHdrFormat
{
    None,
    Hdr,
    Hdr10,
    Hdr10Plus,
    DolbyVision
}

public enum AnimeAudioCodec
{
    Unknown,
    Aac,
    Flac,
    Opus,
    Ac3,
    Eac3,
    Dts,
    DtsHd,
    TrueHd
}

public sealed record AnimeReleaseEvidence(string Field, string Value, string MatchedText);

public sealed record AnimeReleaseInfo(
    string RawTitle,
    string SeriesTitle,
    int? SeasonNumber,
    int? EpisodeStart,
    int? EpisodeEnd,
    int? AbsoluteEpisodeStart,
    int? AbsoluteEpisodeEnd,
    DateOnly? AirDate,
    bool IsSeasonPack,
    bool IsMultiEpisode,
    int? Resolution,
    AnimeReleaseSource Source,
    AnimeVideoCodec VideoCodec,
    int? BitDepth,
    AnimeHdrFormat HdrFormat,
    AnimeAudioCodec AudioCodec,
    string? AudioChannels,
    IReadOnlyList<string> AudioLanguages,
    IReadOnlyList<string> SubtitleLanguages,
    bool IsDualAudio,
    bool IsMultiAudio,
    string? ReleaseGroup,
    int Version,
    bool IsProper,
    bool IsRepack,
    string? ImdbId,
    string ReleaseKey,
    double Confidence,
    IReadOnlyList<AnimeReleaseEvidence> Evidence);

public static partial class AnimeReleaseParser
{
    private static readonly IReadOnlyDictionary<string, string> LanguageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JA"] = "JA",
            ["JPN"] = "JA",
            ["JP"] = "JA",
            ["EN"] = "EN",
            ["ENG"] = "EN",
            ["DE"] = "DE",
            ["GER"] = "DE",
            ["DEU"] = "DE",
            ["FR"] = "FR",
            ["FRE"] = "FR",
            ["FRA"] = "FR",
            ["ES"] = "ES",
            ["SPA"] = "ES",
            ["IT"] = "IT",
            ["ITA"] = "IT",
            ["PT"] = "PT",
            ["POR"] = "PT",
            ["ID"] = "ID",
            ["IND"] = "ID",
            ["ZH"] = "ZH",
            ["CHI"] = "ZH",
            ["ZHO"] = "ZH",
            ["KO"] = "KO",
            ["KOR"] = "KO",
            ["AR"] = "AR",
            ["MS"] = "MS",
            ["MAY"] = "MS",
            ["MSA"] = "MS",
            ["PL"] = "PL",
            ["POL"] = "PL",
            ["RU"] = "RU",
            ["RUS"] = "RU",
            ["NL"] = "NL",
            ["DUT"] = "NL",
            ["NLD"] = "NL",
            ["TR"] = "TR",
            ["TUR"] = "TR"
        };

    [GeneratedRegex(@"^\[(?<group>[^\]]+)\][ ._-]*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingGroupRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])S(?<season>\d{1,3})E(?<start>\d{1,4})(?:(?:\s*[-~]\s*E?|E)(?<end>\d{1,4}))?(?:v(?<version>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:S(?<season>\d{1,3})|Season[ ._-]*(?<seasonWord>\d{1,3}))(?=\s|[._\-\[(]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonPackRegex();

    [GeneratedRegex(
        @"\s-\s(?<start>\d{1,4})(?:\s*-\s*(?<end>\d{1,4}))?(?:v(?<version>\d+))?(?=\s|[._\[(]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteEpisodeRegex();

    [GeneratedRegex(
        @"(?<!\d)(?<year>(?:19|20)\d{2})[-.](?<month>0[1-9]|1[0-2])[-.](?<day>0[1-9]|[12]\d|3[01])(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AirDateRegex();

    [GeneratedRegex(@"(?<!\d)(?<resolution>2160|1080|720|576|480)p\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"\bWEB[ ._-]?DL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebDlRegex();

    [GeneratedRegex(@"\bWEB[ ._-]?RIP\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebRipRegex();

    [GeneratedRegex(@"\b(?:BD[ ._-]?RIP|BLU[ ._-]?RAY[ ._-]?RIP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluRayRipRegex();

    [GeneratedRegex(@"\b(?:BLU[ ._-]?RAY|BDMV)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluRayRegex();

    [GeneratedRegex(@"\bHDTV\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HdtvRegex();

    [GeneratedRegex(@"\bAV1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Av1Regex();

    [GeneratedRegex(@"\b(?:X265|H[ ._-]?265|HEVC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HevcRegex();

    [GeneratedRegex(@"\b(?:X264|H[ ._-]?264|AVC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AvcRegex();

    [GeneratedRegex(@"(?<!\d)(?<depth>8|10|12)[ ._-]?bit\b|\bHi10P\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BitDepthRegex();

    [GeneratedRegex(@"\b(?:DOVI|DOLBY[ ._-]?VISION|DV)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DolbyVisionRegex();

    [GeneratedRegex(@"\bHDR10\+(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hdr10PlusRegex();

    [GeneratedRegex(@"\bHDR10\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hdr10Regex();

    [GeneratedRegex(@"\bHDR\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HdrRegex();

    [GeneratedRegex(@"\bTRUE[ ._-]?HD\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrueHdRegex();

    [GeneratedRegex(@"\bDTS[ ._-]?HD(?:[ ._-]?MA)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DtsHdRegex();

    [GeneratedRegex(@"\bDTS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DtsRegex();

    [GeneratedRegex(@"\bFLAC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlacRegex();

    [GeneratedRegex(@"\b(?:EAC3|E-AC-3|DDP|DD\+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Eac3Regex();

    [GeneratedRegex(@"\b(?:AC3|AC-3|DD)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ac3Regex();

    [GeneratedRegex(@"\bAAC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AacRegex();

    [GeneratedRegex(@"\bOPUS\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpusRegex();

    [GeneratedRegex(@"(?<!\d)(?<channels>7\.1|5\.1|2\.0|1\.0)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelsRegex();

    [GeneratedRegex(@"\bDUAL[ ._-]?AUDIO\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DualAudioRegex();

    [GeneratedRegex(@"\bMULTI(?:[ ._-]?AUDIO)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiAudioRegex();

    [GeneratedRegex(@"\bPROPER\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProperRegex();

    [GeneratedRegex(@"\bREPACK\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepackRegex();

    [GeneratedRegex(@"\bv(?<version>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\b(?<imdb>tt\d{5,12})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbRegex();

    [GeneratedRegex(@"\[(?<content>[^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"-(?<group>[A-Za-z0-9][A-Za-z0-9._]{1,40})$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingDashGroupRegex();

    [GeneratedRegex(@"(?<group>[A-Za-z0-9][A-Za-z0-9._-]{1,40})\s+tt\d{5,12}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BeforeImdbGroupRegex();

    public static bool TryParse(string? releaseName, out AnimeReleaseInfo release)
    {
        release = default!;
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return false;
        }

        release = Parse(releaseName);
        return true;
    }

    public static AnimeReleaseInfo Parse(string releaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseName);

        var rawTitle = StripKnownExtension(releaseName.Trim());
        var evidence = new List<AnimeReleaseEvidence>();

        var leadingGroup = LeadingGroupRegex().Match(rawTitle);
        var releaseGroup = leadingGroup.Success && !IsTechnicalBracket(leadingGroup.Groups["group"].Value)
            ? leadingGroup.Groups["group"].Value.Trim()
            : null;

        if (releaseGroup is not null)
        {
            AddEvidence(evidence, "releaseGroup", releaseGroup, leadingGroup.Value);
        }

        var seasonEpisode = SeasonEpisodeRegex().Match(rawTitle);
        var airDateMatch = AirDateRegex().Match(rawTitle);
        var absoluteEpisode = seasonEpisode.Success || airDateMatch.Success
            ? Match.Empty
            : AbsoluteEpisodeRegex().Match(rawTitle);
        var seasonPack = seasonEpisode.Success || absoluteEpisode.Success || airDateMatch.Success
            ? Match.Empty
            : SeasonPackRegex().Match(rawTitle);

        int? seasonNumber = null;
        int? episodeStart = null;
        int? episodeEnd = null;
        int? absoluteStart = null;
        int? absoluteEnd = null;
        DateOnly? airDate = null;
        var version = 1;

        Match? numberingMatch = null;

        if (seasonEpisode.Success)
        {
            numberingMatch = seasonEpisode;
            seasonNumber = ParseInt(seasonEpisode.Groups["season"].Value);
            episodeStart = ParseInt(seasonEpisode.Groups["start"].Value);
            episodeEnd = ParseOptionalInt(seasonEpisode.Groups["end"].Value) ?? episodeStart;
            version = ParseOptionalInt(seasonEpisode.Groups["version"].Value) ?? version;

            AddEvidence(evidence, "season", seasonNumber, seasonEpisode.Value);
            AddEvidence(evidence, "episodeRange", FormatRange(episodeStart, episodeEnd), seasonEpisode.Value);
        }
        else if (absoluteEpisode.Success)
        {
            numberingMatch = absoluteEpisode;
            absoluteStart = ParseInt(absoluteEpisode.Groups["start"].Value);
            absoluteEnd = ParseOptionalInt(absoluteEpisode.Groups["end"].Value) ?? absoluteStart;
            version = ParseOptionalInt(absoluteEpisode.Groups["version"].Value) ?? version;

            AddEvidence(evidence, "absoluteEpisodeRange", FormatRange(absoluteStart, absoluteEnd), absoluteEpisode.Value);
        }
        else if (airDateMatch.Success &&
                 DateOnly.TryParseExact(
                     $"{airDateMatch.Groups["year"].Value}-{airDateMatch.Groups["month"].Value}-{airDateMatch.Groups["day"].Value}",
                     "yyyy-MM-dd",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out var parsedDate))
        {
            numberingMatch = airDateMatch;
            airDate = parsedDate;
            AddEvidence(evidence, "airDate", parsedDate.ToString("yyyy-MM-dd"), airDateMatch.Value);
        }
        else if (seasonPack.Success)
        {
            numberingMatch = seasonPack;
            seasonNumber = ParseOptionalInt(seasonPack.Groups["season"].Value) ??
                           ParseOptionalInt(seasonPack.Groups["seasonWord"].Value);
            AddEvidence(evidence, "seasonPack", seasonNumber, seasonPack.Value);
        }

        var globalVersion = VersionRegex().Match(rawTitle);
        if (globalVersion.Success)
        {
            version = ParseInt(globalVersion.Groups["version"].Value);
            AddEvidence(evidence, "version", version, globalVersion.Value);
        }

        var seriesTitle = ExtractSeriesTitle(rawTitle, leadingGroup, numberingMatch);

        var resolutionMatch = ResolutionRegex().Match(rawTitle);
        int? resolution = resolutionMatch.Success ? ParseInt(resolutionMatch.Groups["resolution"].Value) : null;
        if (resolution is not null)
        {
            AddEvidence(evidence, "resolution", $"{resolution}p", resolutionMatch.Value);
        }

        var (source, sourceMatch) = DetectSource(rawTitle);
        AddMatchEvidence(evidence, "source", source.ToString(), sourceMatch);

        var (videoCodec, videoMatch) = DetectVideoCodec(rawTitle);
        AddMatchEvidence(evidence, "videoCodec", videoCodec.ToString(), videoMatch);

        var bitDepthMatch = BitDepthRegex().Match(rawTitle);
        int? bitDepth = null;
        if (bitDepthMatch.Success)
        {
            bitDepth = bitDepthMatch.Value.Equals("Hi10P", StringComparison.OrdinalIgnoreCase)
                ? 10
                : ParseOptionalInt(bitDepthMatch.Groups["depth"].Value);
            AddEvidence(evidence, "bitDepth", bitDepth, bitDepthMatch.Value);
        }

        var (hdrFormat, hdrMatch) = DetectHdr(rawTitle);
        if (hdrFormat != AnimeHdrFormat.None)
        {
            AddMatchEvidence(evidence, "hdr", hdrFormat.ToString(), hdrMatch);
        }

        var (audioCodec, audioMatch) = DetectAudioCodec(rawTitle);
        AddMatchEvidence(evidence, "audioCodec", audioCodec.ToString(), audioMatch);

        var channelsMatch = ChannelsRegex().Match(rawTitle);
        var audioChannels = channelsMatch.Success ? channelsMatch.Groups["channels"].Value : null;
        if (audioChannels is not null)
        {
            AddEvidence(evidence, "audioChannels", audioChannels, channelsMatch.Value);
        }

        var (audioLanguages, subtitleLanguages) = DetectLanguages(rawTitle, audioMatch);
        if (audioLanguages.Count > 0)
        {
            AddEvidence(evidence, "audioLanguages", string.Join("+", audioLanguages), string.Join("+", audioLanguages));
        }

        if (subtitleLanguages.Count > 0)
        {
            AddEvidence(evidence, "subtitleLanguages", string.Join("+", subtitleLanguages), string.Join("+", subtitleLanguages));
        }

        var dualAudioMatch = DualAudioRegex().Match(rawTitle);
        var multiAudioMatch = MultiAudioRegex().Match(rawTitle);
        var isDualAudio = dualAudioMatch.Success || audioLanguages.Count == 2;
        var isMultiAudio = multiAudioMatch.Success || audioLanguages.Count > 2;
        if (dualAudioMatch.Success)
        {
            AddEvidence(evidence, "dualAudio", true, dualAudioMatch.Value);
        }

        if (multiAudioMatch.Success)
        {
            AddEvidence(evidence, "multiAudio", true, multiAudioMatch.Value);
        }

        var properMatch = ProperRegex().Match(rawTitle);
        var repackMatch = RepackRegex().Match(rawTitle);
        if (properMatch.Success)
        {
            AddEvidence(evidence, "proper", true, properMatch.Value);
        }

        if (repackMatch.Success)
        {
            AddEvidence(evidence, "repack", true, repackMatch.Value);
        }

        var imdbMatch = ImdbRegex().Match(rawTitle);
        var imdbId = imdbMatch.Success ? imdbMatch.Groups["imdb"].Value.ToLowerInvariant() : null;
        if (imdbId is not null)
        {
            AddEvidence(evidence, "imdbId", imdbId, imdbMatch.Value);
        }

        if (releaseGroup is null)
        {
            var beforeImdb = BeforeImdbGroupRegex().Match(rawTitle);
            if (beforeImdb.Success && !IsTechnicalToken(beforeImdb.Groups["group"].Value))
            {
                releaseGroup = beforeImdb.Groups["group"].Value;
                AddEvidence(evidence, "releaseGroup", releaseGroup, beforeImdb.Groups["group"].Value);
            }
            else
            {
                var trailing = TrailingDashGroupRegex().Match(rawTitle);
                if (trailing.Success && !IsTechnicalToken(trailing.Groups["group"].Value))
                {
                    releaseGroup = trailing.Groups["group"].Value;
                    AddEvidence(evidence, "releaseGroup", releaseGroup, trailing.Value);
                }
            }
        }

        var isSeasonPack = seasonPack.Success;
        var isMultiEpisode =
            (episodeStart is not null && episodeEnd is not null && episodeEnd != episodeStart) ||
            (absoluteStart is not null && absoluteEnd is not null && absoluteEnd != absoluteStart);

        var confidence = CalculateConfidence(
            seriesTitle,
            seasonEpisode.Success || absoluteEpisode.Success || airDateMatch.Success || seasonPack.Success,
            resolution is not null,
            source != AnimeReleaseSource.Unknown,
            videoCodec != AnimeVideoCodec.Unknown,
            audioCodec != AnimeAudioCodec.Unknown,
            releaseGroup is not null);

        return new AnimeReleaseInfo(
            rawTitle,
            seriesTitle,
            seasonNumber,
            episodeStart,
            episodeEnd,
            absoluteStart,
            absoluteEnd,
            airDate,
            isSeasonPack,
            isMultiEpisode,
            resolution,
            source,
            videoCodec,
            bitDepth,
            hdrFormat,
            audioCodec,
            audioChannels,
            audioLanguages,
            subtitleLanguages,
            isDualAudio,
            isMultiAudio,
            releaseGroup,
            version,
            properMatch.Success,
            repackMatch.Success,
            imdbId,
            BuildReleaseKey(rawTitle),
            confidence,
            evidence);
    }

    private static string ExtractSeriesTitle(string rawTitle, Match leadingGroup, Match? numberingMatch)
    {
        var titleStart = leadingGroup.Success ? leadingGroup.Index + leadingGroup.Length : 0;
        var titleEnd = numberingMatch is { Success: true } ? numberingMatch.Index : FindTechnicalSuffixStart(rawTitle);

        if (titleEnd < titleStart)
        {
            titleEnd = rawTitle.Length;
        }

        var candidate = rawTitle[titleStart..titleEnd];
        candidate = Regex.Replace(candidate, @"[._]+", " ");
        candidate = Regex.Replace(candidate, @"\s+", " ");
        return candidate.Trim(' ', '-', '_', '.', '[', ']', '(', ')');
    }

    private static int FindTechnicalSuffixStart(string rawTitle)
    {
        var matches = new[]
        {
            ResolutionRegex().Match(rawTitle),
            WebDlRegex().Match(rawTitle),
            WebRipRegex().Match(rawTitle),
            BluRayRipRegex().Match(rawTitle),
            BluRayRegex().Match(rawTitle),
            HdtvRegex().Match(rawTitle),
            Av1Regex().Match(rawTitle),
            HevcRegex().Match(rawTitle),
            AvcRegex().Match(rawTitle)
        };

        return matches.Where(match => match.Success).Select(match => match.Index).DefaultIfEmpty(rawTitle.Length).Min();
    }

    private static (AnimeReleaseSource Source, Match Match) DetectSource(string value)
    {
        var match = WebDlRegex().Match(value);
        if (match.Success) return (AnimeReleaseSource.WebDl, match);

        match = WebRipRegex().Match(value);
        if (match.Success) return (AnimeReleaseSource.WebRip, match);

        match = BluRayRipRegex().Match(value);
        if (match.Success) return (AnimeReleaseSource.BluRayRip, match);

        match = BluRayRegex().Match(value);
        if (match.Success) return (AnimeReleaseSource.BluRay, match);

        match = HdtvRegex().Match(value);
        return match.Success ? (AnimeReleaseSource.Hdtv, match) : (AnimeReleaseSource.Unknown, Match.Empty);
    }

    private static (AnimeVideoCodec Codec, Match Match) DetectVideoCodec(string value)
    {
        var match = Av1Regex().Match(value);
        if (match.Success) return (AnimeVideoCodec.Av1, match);

        match = HevcRegex().Match(value);
        if (match.Success) return (AnimeVideoCodec.Hevc, match);

        match = AvcRegex().Match(value);
        return match.Success ? (AnimeVideoCodec.Avc, match) : (AnimeVideoCodec.Unknown, Match.Empty);
    }

    private static (AnimeHdrFormat Format, Match Match) DetectHdr(string value)
    {
        var match = DolbyVisionRegex().Match(value);
        if (match.Success) return (AnimeHdrFormat.DolbyVision, match);

        match = Hdr10PlusRegex().Match(value);
        if (match.Success) return (AnimeHdrFormat.Hdr10Plus, match);

        match = Hdr10Regex().Match(value);
        if (match.Success) return (AnimeHdrFormat.Hdr10, match);

        match = HdrRegex().Match(value);
        return match.Success ? (AnimeHdrFormat.Hdr, match) : (AnimeHdrFormat.None, Match.Empty);
    }

    private static (AnimeAudioCodec Codec, Match Match) DetectAudioCodec(string value)
    {
        var match = TrueHdRegex().Match(value);
        if (match.Success) return (AnimeAudioCodec.TrueHd, match);

        match = DtsHdRegex().Match(value);
        if (match.Success) return (AnimeAudioCodec.DtsHd, match);

        match = DtsRegex().Match(value);
        if (match.Success) return (AnimeAudioCodec.Dts, match);

        match = FlacRegex().Match(value);
        if (match.Success) return (AnimeAudioCodec.Flac, match);

        match = Eac3Regex().Match(value);
        if (match.Success) return (AnimeAudioCodec.Eac3, match);

        match = Ac3Regex().Match(value);
        if (match.Success) return (AnimeAudioCodec.Ac3, match);

        match = AacRegex().Match(value);
        if (match.Success) return (AnimeAudioCodec.Aac, match);

        match = OpusRegex().Match(value);
        return match.Success ? (AnimeAudioCodec.Opus, match) : (AnimeAudioCodec.Unknown, Match.Empty);
    }

    private static (IReadOnlyList<string> Audio, IReadOnlyList<string> Subtitles) DetectLanguages(
        string rawTitle,
        Match audioCodecMatch)
    {
        var languageGroups = new List<(Match Match, IReadOnlyList<string> Languages)>();
        foreach (Match bracket in BracketRegex().Matches(rawTitle))
        {
            var languages = ParseLanguageGroup(bracket.Groups["content"].Value);
            if (languages.Count > 0)
            {
                languageGroups.Add((bracket, languages));
            }
        }

        if (languageGroups.Count == 0)
        {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var audio = new List<string>();
        var subtitles = new List<string>();

        if (audioCodecMatch.Success)
        {
            var nearest = languageGroups
                .Where(group => group.Match.Index >= audioCodecMatch.Index + audioCodecMatch.Length)
                .OrderBy(group => group.Match.Index)
                .FirstOrDefault();

            if (nearest.Match is not null &&
                nearest.Match.Index - (audioCodecMatch.Index + audioCodecMatch.Length) <= 6)
            {
                audio.AddRange(nearest.Languages);
                languageGroups.Remove(nearest);
            }
        }

        foreach (var group in languageGroups)
        {
            foreach (var language in group.Languages)
            {
                if (!subtitles.Contains(language, StringComparer.OrdinalIgnoreCase))
                {
                    subtitles.Add(language);
                }
            }
        }

        return (audio, subtitles);
    }

    private static IReadOnlyList<string> ParseLanguageGroup(string content)
    {
        var tokens = Regex.Split(content.Trim(), @"[+,/&\s]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (tokens.Length == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        foreach (var token in tokens)
        {
            if (!LanguageAliases.TryGetValue(token, out var language))
            {
                return Array.Empty<string>();
            }

            if (!normalized.Contains(language, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(language);
            }
        }

        return normalized;
    }

    private static bool IsTechnicalBracket(string value) =>
        ParseLanguageGroup(value).Count > 0 ||
        ResolutionRegex().IsMatch(value) ||
        BitDepthRegex().IsMatch(value) ||
        Av1Regex().IsMatch(value) ||
        HevcRegex().IsMatch(value) ||
        AvcRegex().IsMatch(value);

    private static bool IsTechnicalToken(string value) =>
        value.Equals("Proper", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Repack", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
        ResolutionRegex().IsMatch(value) ||
        WebDlRegex().IsMatch(value) ||
        WebRipRegex().IsMatch(value) ||
        BluRayRegex().IsMatch(value) ||
        BluRayRipRegex().IsMatch(value) ||
        HdtvRegex().IsMatch(value) ||
        Av1Regex().IsMatch(value) ||
        HevcRegex().IsMatch(value) ||
        AvcRegex().IsMatch(value);

    private static string StripKnownExtension(string value)
    {
        var extension = Path.GetExtension(value);
        if (extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".m2ts", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".nzb", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^extension.Length];
        }

        return value;
    }

    private static string BuildReleaseKey(string rawTitle)
    {
        var normalized = Regex.Replace(rawTitle.Normalize(), @"\s+", " ").Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static double CalculateConfidence(
        string seriesTitle,
        bool hasNumbering,
        bool hasResolution,
        bool hasSource,
        bool hasVideoCodec,
        bool hasAudioCodec,
        bool hasReleaseGroup)
    {
        var score = string.IsNullOrWhiteSpace(seriesTitle) ? 0d : 0.2d;
        if (hasNumbering) score += 0.35d;
        if (hasResolution) score += 0.1d;
        if (hasSource) score += 0.1d;
        if (hasVideoCodec) score += 0.1d;
        if (hasAudioCodec) score += 0.05d;
        if (hasReleaseGroup) score += 0.1d;
        return Math.Min(1d, score);
    }

    private static string FormatRange(int? start, int? end) =>
        start == end ? start?.ToString() ?? "" : $"{start}-{end}";

    private static int ParseInt(string value) => int.Parse(value);

    private static int? ParseOptionalInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static void AddMatchEvidence(
        ICollection<AnimeReleaseEvidence> evidence,
        string field,
        string value,
        Match match)
    {
        if (match.Success)
        {
            AddEvidence(evidence, field, value, match.Value);
        }
    }

    private static void AddEvidence(
        ICollection<AnimeReleaseEvidence> evidence,
        string field,
        object? value,
        string matchedText)
    {
        if (value is not null)
        {
            evidence.Add(new AnimeReleaseEvidence(field, value.ToString() ?? "", matchedText));
        }
    }
}
