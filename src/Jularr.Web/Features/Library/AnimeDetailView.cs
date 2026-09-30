using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Playback;

namespace Jularr.Web.Features.Library;

/// <summary>How the episodes of the selected season are ordered.</summary>
public enum AnimeEpisodeSort
{
    EpisodeAscending,
    EpisodeDescending,
    UnwatchedFirst
}

/// <summary>Whether the episodes of the selected season are cards or compact rows.</summary>
public enum AnimeEpisodeLayout
{
    Grid,
    List
}

/// <summary>Where an episode stands for the profile looking at the page.</summary>
public enum AnimeEpisodeAvailability
{
    /// <summary>A file is in the library and it fits the profile's language preference (or there is none).</summary>
    Available,

    /// <summary>A file is in the library, but only in languages other than the preferred ones.</summary>
    OtherLanguageOnly,

    /// <summary>No file yet; the title has an open request that has not started downloading.</summary>
    Requested,

    Downloading,

    Importing,

    /// <summary>No file and nothing on its way.</summary>
    Unavailable
}

/// <summary>The primary play action of the page: what pressing it does with the next episode.</summary>
public enum AnimePrimaryAction
{
    Continue,
    Start,
    WatchAgain
}

/// <summary>What the media inventory knows about one episode: whether it has a file, how long it runs and the languages it carries.</summary>
public sealed record AnimeEpisodeMediaFacts(
    bool HasFile,
    int? RuntimeMinutes,
    IReadOnlySet<string> AudioLanguages,
    IReadOnlySet<string> SubtitleLanguages)
{
    public static AnimeEpisodeMediaFacts None { get; } = new(false, null, new HashSet<string>(), new HashSet<string>());
}

/// <summary>One language code on an episode card; preferred ones are highlighted.</summary>
public sealed record AnimeLanguageChip(string Code, bool IsPreferred);

/// <summary>A short list of language chips plus how many more did not fit.</summary>
public sealed record AnimeLanguageChips(IReadOnlyList<AnimeLanguageChip> Shown, int More)
{
    public static AnimeLanguageChips Empty { get; } = new([], 0);

    public bool Any => Shown.Count > 0;
}

/// <summary>An entry of the season rail: one season, the specials, or an owner-defined display group.</summary>
/// <param name="Key">Stable key used in the address: <c>s1</c>, <c>s0</c> or <c>g0</c>.</param>
/// <param name="SeasonNumber">The season number for season entries; null for groups.</param>
/// <param name="GroupName">The name of an owner-defined display group; null for seasons.</param>
public sealed record AnimeStructureEntry(
    string Key,
    int? SeasonNumber,
    string? GroupName,
    int EpisodeCount)
{
    public bool IsSpecials => SeasonNumber == 0;
}

/// <summary>
/// Pure decisions behind the Anime/Series detail page (docs/mockups/anime-series-detail/SPEC.md): the
/// season structure, which episode the primary action opens, and the per-episode language and
/// availability state. Nothing here reads the database, so every rule can be tested on its own.
/// </summary>
public static class AnimeDetailView
{
    /// <summary>The most language chips shown per group on an episode card; the rest is a "+n".</summary>
    public const int MaxLanguageChips = 3;

    public static AnimeEpisodeSort ParseSort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "desc" => AnimeEpisodeSort.EpisodeDescending,
        "unwatched" => AnimeEpisodeSort.UnwatchedFirst,
        _ => AnimeEpisodeSort.EpisodeAscending
    };

    /// <summary>The address value of a sort; the default sort has none.</summary>
    public static string? SortName(AnimeEpisodeSort sort) => sort switch
    {
        AnimeEpisodeSort.EpisodeDescending => "desc",
        AnimeEpisodeSort.UnwatchedFirst => "unwatched",
        _ => null
    };

    public static AnimeEpisodeLayout ParseLayout(string? value) =>
        string.Equals(value?.Trim(), "list", StringComparison.OrdinalIgnoreCase)
            ? AnimeEpisodeLayout.List
            : AnimeEpisodeLayout.Grid;

    public static string? LayoutName(AnimeEpisodeLayout layout) =>
        layout == AnimeEpisodeLayout.List ? "list" : null;

    /// <summary>Orders a season's episodes; the sort is stable, so episodes keep their number order inside a tie.</summary>
    public static IReadOnlyList<T> Sort<T>(
        IEnumerable<T> episodes,
        AnimeEpisodeSort sort,
        Func<T, int> number,
        Func<T, bool> isWatched) =>
        sort switch
        {
            AnimeEpisodeSort.EpisodeDescending => [.. episodes.OrderByDescending(number)],
            AnimeEpisodeSort.UnwatchedFirst => [.. episodes.OrderBy(isWatched).ThenBy(number)],
            _ => [.. episodes.OrderBy(number)]
        };

    /// <summary>
    /// The season rail of a work with its regular seasons first and the specials (season 0) last. Seasons that
    /// hold no episode never appear.
    /// </summary>
    public static IReadOnlyList<AnimeStructureEntry> SeasonEntries(IEnumerable<int> episodeSeasons) =>
    [
        .. episodeSeasons
            .GroupBy(season => season)
            .OrderBy(group => group.Key == 0 ? 1 : 0)
            .ThenBy(group => group.Key)
            .Select(group => new AnimeStructureEntry($"s{group.Key}", group.Key, null, group.Count()))
    ];

    /// <summary>The entries for owner-defined display groups, in the order of the groups.</summary>
    public static IReadOnlyList<AnimeStructureEntry> GroupEntries(IEnumerable<(string Name, int EpisodeCount)> groups) =>
    [
        .. groups.Select((group, index) => new AnimeStructureEntry($"g{index}", null, group.Name, group.EpisodeCount))
    ];

    /// <summary>
    /// The entry shown: the requested one when it exists, otherwise the one holding the episode the primary
    /// action opens, otherwise the first.
    /// </summary>
    public static AnimeStructureEntry? SelectEntry(
        IReadOnlyList<AnimeStructureEntry> entries,
        string? requestedKey,
        string? keyOfNextEpisode)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        return entries.FirstOrDefault(entry => string.Equals(entry.Key, requestedKey, StringComparison.Ordinal))
            ?? entries.FirstOrDefault(entry => string.Equals(entry.Key, keyOfNextEpisode, StringComparison.Ordinal))
            ?? entries[0];
    }

    /// <summary>
    /// The episode the primary action opens: one being watched, else the first unwatched one (regular seasons
    /// before the specials), else the first episode again once everything is watched. Null without episodes.
    /// </summary>
    public static (T? Episode, AnimePrimaryAction Action) ChoosePrimary<T>(
        IReadOnlyList<T> episodesInOrder,
        Func<T, int> seasonNumber,
        Func<T, bool> isWatched,
        Func<T, int?> resumePercent)
        where T : class
    {
        if (episodesInOrder.Count == 0)
        {
            return (null, AnimePrimaryAction.Start);
        }

        var resuming = episodesInOrder.FirstOrDefault(episode => resumePercent(episode) is not null);
        if (resuming is not null)
        {
            return (resuming, AnimePrimaryAction.Continue);
        }

        var unwatched = episodesInOrder
            .Where(episode => !isWatched(episode))
            .OrderBy(episode => seasonNumber(episode) == 0 ? 1 : 0)
            .FirstOrDefault();
        if (unwatched is null)
        {
            return (episodesInOrder[0], AnimePrimaryAction.WatchAgain);
        }

        return (unwatched, episodesInOrder.Any(isWatched) ? AnimePrimaryAction.Continue : AnimePrimaryAction.Start);
    }

    /// <summary>
    /// Where an episode stands. With a file the languages decide: when the profile prefers an audio or subtitle
    /// language and the file carries neither of them (but carries others), only other languages are available.
    /// Without a file the open request for the title says what is on its way.
    /// </summary>
    public static AnimeEpisodeAvailability Availability(
        AnimeEpisodeMediaFacts facts,
        AcquisitionRequestStatus? openRequest,
        string? preferredAudio,
        string? preferredSubtitle)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!facts.HasFile)
        {
            return openRequest switch
            {
                AcquisitionRequestStatus.Downloading => AnimeEpisodeAvailability.Downloading,
                AcquisitionRequestStatus.Importing => AnimeEpisodeAvailability.Importing,
                AcquisitionRequestStatus.Pending
                    or AcquisitionRequestStatus.Approved
                    or AcquisitionRequestStatus.Searching => AnimeEpisodeAvailability.Requested,
                _ => AnimeEpisodeAvailability.Unavailable
            };
        }

        var audio = PlaybackLanguages.Normalize(preferredAudio);
        var subtitle = PlaybackLanguages.Normalize(preferredSubtitle);
        var wantsSubtitle = subtitle is not null && subtitle != PlaybackLanguages.SubtitlesOff;
        var hasOtherLanguages = facts.AudioLanguages.Count > 0 || facts.SubtitleLanguages.Count > 0;
        if ((audio is null && !wantsSubtitle) || !hasOtherLanguages)
        {
            return AnimeEpisodeAvailability.Available;
        }

        var satisfied = (audio is not null && facts.AudioLanguages.Contains(audio))
            || (wantsSubtitle && facts.SubtitleLanguages.Contains(subtitle!));
        return satisfied ? AnimeEpisodeAvailability.Available : AnimeEpisodeAvailability.OtherLanguageOnly;
    }

    /// <summary>The catalog key of an availability label.</summary>
    public static string AvailabilityKey(AnimeEpisodeAvailability availability) => availability switch
    {
        AnimeEpisodeAvailability.Available => "library.mediaCard.availability.available",
        AnimeEpisodeAvailability.OtherLanguageOnly => "library.anime.availability.otherLanguage",
        AnimeEpisodeAvailability.Requested => "library.anime.availability.requested",
        AnimeEpisodeAvailability.Downloading => "library.anime.availability.downloading",
        AnimeEpisodeAvailability.Importing => "library.anime.availability.importing",
        _ => "library.anime.availability.unavailable"
    };

    /// <summary>The style modifier and icon of an availability, for example <c>available</c> or <c>other-language</c>.</summary>
    public static string AvailabilityName(AnimeEpisodeAvailability availability) => availability switch
    {
        AnimeEpisodeAvailability.Available => "available",
        AnimeEpisodeAvailability.OtherLanguageOnly => "other-language",
        AnimeEpisodeAvailability.Requested => "requested",
        AnimeEpisodeAvailability.Downloading => "downloading",
        AnimeEpisodeAvailability.Importing => "importing",
        _ => "unavailable"
    };

    /// <summary>The languages of one kind as card chips: the preferred one first, then by code, capped for the card.</summary>
    public static AnimeLanguageChips Chips(IReadOnlySet<string> languages, string? preferred)
    {
        ArgumentNullException.ThrowIfNull(languages);

        var wanted = PlaybackLanguages.Normalize(preferred);
        var ordered = languages
            .OrderBy(language => string.Equals(language, wanted, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(language => language, StringComparer.Ordinal)
            .ToArray();
        return new AnimeLanguageChips(
            [.. ordered.Take(MaxLanguageChips).Select(language => new AnimeLanguageChip(
                language.ToUpperInvariant(),
                string.Equals(language, wanted, StringComparison.Ordinal)))],
            Math.Max(0, ordered.Length - MaxLanguageChips));
    }

    /// <summary>Episode runtime in whole minutes from the file's duration; null when unknown.</summary>
    public static int? RuntimeMinutes(double? durationSeconds) =>
        durationSeconds is > 0 ? Math.Max(1, (int)Math.Round(durationSeconds.Value / 60d)) : null;

    /// <summary>The label an episode carries in lists: <c>S01 E03</c>.</summary>
    public static string EpisodeCode(int season, int number) => $"S{season:00} E{number:00}";
}
