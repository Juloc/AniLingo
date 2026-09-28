using System.Globalization;
using Jularr.Web.Features.Localization;
using Jularr.Web.Ui;

namespace Jularr.Web.Features.MediaFacts;

/// <summary>One coloured chip in a language-availability group, e.g. "DE 18/24".</summary>
/// <param name="Code">Upper-cased language tag, matching the Library card chip convention.</param>
/// <param name="Fraction">"available/total", or null when there is only one known unit to begin with.</param>
/// <param name="IsPartial">Whether this language covers only some of the known units.</param>
/// <param name="PartialLabel">Localized "Partial" qualifier for the tooltip/screen reader text, or null when complete.</param>
public sealed record MediaFactsLanguageChip(string Code, string? Fraction, bool IsPartial, string? PartialLabel);

/// <summary>One icon group of language chips: audio, subtitles, or reading/original text.</summary>
public sealed record MediaFactsLanguageGroup(string Label, MediaFactsLanguageUsage Usage, IReadOnlyList<MediaFactsLanguageChip> Chips);

/// <summary>
/// Display-ready view of a <see cref="MediaFacts"/> projection for <c>_MediaFacts.cshtml</c>,
/// built with <see cref="Create"/> exactly like <see cref="MediaBannerCardModel.Create"/> builds a
/// card: every list renders only what is present.
/// </summary>
public sealed record MediaFactsStripModel(
    string FactsAriaLabel,
    MediaBannerStatusView? Status,
    IReadOnlyList<string> Chips,
    string LanguagesAriaLabel,
    IReadOnlyList<MediaFactsLanguageGroup> LanguageGroups)
{
    public bool HasFacts => Status is not null || Chips.Count > 0;
    public bool HasLanguages => LanguageGroups.Count > 0;

    /// <param name="showFacts">
    /// False on pages whose existing header already shows status/counts/year in its own words
    /// (every detail page wired up so far does); the strip then renders language availability
    /// only, so nothing is ever shown twice (#426).
    /// </param>
    public static MediaFactsStripModel Create(
        MediaFacts facts,
        UiTextBundle ui,
        bool showFacts = true,
        bool showLanguages = true)
    {
        var status = showFacts && facts.Status is { } releaseStatus
            ? new MediaBannerStatusView(releaseStatus, ui[StatusKey(releaseStatus)])
            : null;

        var chips = new List<string>();
        if (showFacts)
        {
            if (facts.ReleaseYear is int year and > 0)
            {
                chips.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            switch (facts.Kind)
            {
                case MediaBannerKind.Anime:
                    AddCount(chips, facts.PrimaryUnitCount, "mediaFacts.episodeCount", ui);
                    AddCount(chips, facts.SecondaryUnitCount, "mediaFacts.seasonCount", ui);
                    break;
                case MediaBannerKind.Manga:
                case MediaBannerKind.LightNovel:
                    AddCount(chips, facts.PrimaryUnitCount, "mediaFacts.chapterCount", ui);
                    AddCount(chips, facts.SecondaryUnitCount, "mediaFacts.volumeCount", ui);
                    break;
            }

            if (facts.RuntimeMinutes is int minutes and > 0)
            {
                chips.Add(ui.Format("library.anime.durationMinutes", ("minutes", minutes)));
            }
        }

        var groups = new List<MediaFactsLanguageGroup>();
        if (showLanguages)
        {
            AddGroup(groups, facts.Languages, MediaFactsLanguageUsage.Audio, ui["mediaFacts.audio"], ui);
            AddGroup(groups, facts.Languages, MediaFactsLanguageUsage.Subtitle, ui["mediaFacts.subtitles"], ui);
            AddGroup(groups, facts.Languages, MediaFactsLanguageUsage.Text, ui["mediaFacts.text"], ui);
        }

        return new MediaFactsStripModel(
            ui["mediaFacts.factsAria"],
            status,
            chips,
            ui["mediaFacts.languagesAria"],
            groups);
    }

    private static void AddCount(List<string> chips, int? count, string key, UiTextBundle ui)
    {
        if (count is int value and > 0)
        {
            chips.Add(ui.Format(key, ("count", value)));
        }
    }

    private static void AddGroup(
        List<MediaFactsLanguageGroup> groups,
        IReadOnlyList<MediaFactsLanguageRow> rows,
        MediaFactsLanguageUsage usage,
        string label,
        UiTextBundle ui)
    {
        var chips = rows
            .Where(x => x.Usage == usage)
            .OrderByDescending(x => x.AvailableUnits)
            .ThenBy(x => x.Language, StringComparer.Ordinal)
            .Select(x =>
            {
                var isPartial = x.Coverage == MediaFactsCoverage.Partial;
                return new MediaFactsLanguageChip(
                    x.Language.ToUpperInvariant(),
                    x.TotalUnits > 1
                        ? ui.Format("mediaFacts.coverageFraction", ("available", x.AvailableUnits), ("total", x.TotalUnits))
                        : null,
                    isPartial,
                    isPartial ? ui["mediaFacts.coveragePartial"] : null);
            })
            .ToArray();

        if (chips.Length > 0)
        {
            groups.Add(new MediaFactsLanguageGroup(label, usage, chips));
        }
    }

    // Mirrors MediaBannerCardModel.StatusKey (Jularr.Web.Ui): kept as its own small switch rather
    // than reused across features, since that model belongs to the media-card component this
    // strip must not modify (#426, #536).
    private static string StatusKey(MediaReleaseStatus status) => status switch
    {
        MediaReleaseStatus.Ongoing => "library.mediaCard.status.ongoing",
        MediaReleaseStatus.Finished => "library.mediaCard.status.finished",
        MediaReleaseStatus.Upcoming => "library.mediaCard.status.upcoming",
        MediaReleaseStatus.Hiatus => "library.mediaCard.status.hiatus",
        _ => "library.mediaCard.status.cancelled"
    };
}
