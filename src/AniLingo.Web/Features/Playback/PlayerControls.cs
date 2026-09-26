using System.Globalization;
using AniLingo.Web.Features.Progress;

namespace AniLingo.Web.Features.Playback;

/// <summary>One selectable audio or subtitle entry, keyed by its canonical track id.</summary>
public sealed record PlayerTrackOption(
    string Id,
    string Label,
    string? Language,
    bool IsSelectable,
    bool IsLearningSource);

/// <summary>
/// Server-resolved initial state of the web player controls. Initial values
/// come from the profile preferences resolved against the canonical media
/// inventory; the browser keeps later changes for the playback session only.
/// </summary>
public sealed record PlayerControls(
    IReadOnlyList<PlayerTrackOption> AudioTracks,
    string? FileDefaultAudioTrackId,
    string? InitialAudioTrackId,
    IReadOnlyList<PlayerTrackOption> SubtitleTracks,
    bool HasLearningCues,
    string InitialSubtitle,
    IReadOnlyList<double> Speeds,
    double InitialSpeed,
    int? SourceHeight,
    IReadOnlyList<PlaybackStreamVariant> Variants,
    PlaybackPreferencesSnapshot Preferences)
{
    public const string SubtitleOff = "off";
    public const string SubtitleLearning = "learning";

    /// <param name="learningSourceStreamIndex">
    /// Embedded stream that already is the active learning source; choosing it
    /// as playback subtitle shows the learning overlay instead of a duplicate line.
    /// </param>
    public static PlayerControls Build(
        PlaybackMedia media,
        bool hasLearningCues,
        int? learningSourceStreamIndex,
        PlaybackPreferencesSnapshot preferences)
    {
        var tracks = media.Tracks ?? [];
        var audio = tracks
            .Where(x => x.Kind == PlaybackTrackKind.Audio)
            .OrderBy(x => x.StreamIndex)
            .Select((track, position) => new PlayerTrackOption(
                PlaybackTrackIds.Format(track.StreamIndex),
                Label(track, $"Audio {position + 1}"),
                PlaybackLanguages.Normalize(track.Language),
                true,
                false))
            .ToArray();
        var subtitles = tracks
            .Where(x => x.Kind == PlaybackTrackKind.Subtitle)
            .OrderBy(x => x.StreamIndex)
            .Select((track, position) => new PlayerTrackOption(
                PlaybackTrackIds.Format(track.StreamIndex),
                Label(track, $"Subtitle {position + 1}") + (track.IsText ? "" : " · image, not shown"),
                PlaybackLanguages.Normalize(track.Language),
                track.IsText,
                hasLearningCues && track.StreamIndex == learningSourceStreamIndex))
            .ToArray();

        var fileDefaultAudio = PlaybackTrackSelection.DefaultAudio(tracks);
        var initialAudio = PlaybackTrackSelection.ResolveAudio(tracks, preferences.PreferredAudioLanguage);
        var subtitle = PlaybackTrackSelection.ResolveSubtitle(
            tracks,
            hasLearningCues,
            preferences.PreferredSubtitleLanguage);
        var initialSubtitle = subtitle.Mode switch
        {
            PlaybackSubtitleMode.Learning => SubtitleLearning,
            PlaybackSubtitleMode.Embedded when hasLearningCues && subtitle.StreamIndex == learningSourceStreamIndex =>
                SubtitleLearning,
            PlaybackSubtitleMode.Embedded => subtitle.TrackId!,
            _ => SubtitleOff
        };

        return new PlayerControls(
            audio,
            fileDefaultAudio is null ? null : PlaybackTrackIds.Format(fileDefaultAudio.StreamIndex),
            initialAudio is null ? null : PlaybackTrackIds.Format(initialAudio.StreamIndex),
            subtitles,
            hasLearningCues,
            initialSubtitle,
            PlaybackPreferenceRules.Speeds,
            preferences.DefaultPlaybackSpeed,
            media.VideoHeight,
            PlaybackService.DescribeVariants(media),
            preferences);
    }

    private static string Label(PlaybackMediaTrack track, string fallback)
    {
        var language = LanguageName(track.Language);
        var title = string.IsNullOrWhiteSpace(track.Title) ? null : track.Title.Trim();
        var name = (title, language) switch
        {
            ({ } t, { } l) when !t.Contains(l, StringComparison.OrdinalIgnoreCase) => $"{t} ({l})",
            ({ } t, _) => t,
            (null, { } l) => l,
            _ => fallback
        };

        return track.IsForced ? $"{name} · forced" : name;
    }

    private static string? LanguageName(string? language)
    {
        var normalized = PlaybackLanguages.Normalize(language);
        if (normalized is null or PlaybackLanguages.SubtitlesOff)
        {
            return null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            return string.IsNullOrWhiteSpace(culture.EnglishName) || culture.EnglishName.StartsWith("Unknown", StringComparison.Ordinal)
                ? normalized
                : culture.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return normalized;
        }
    }
}
