using AniLingo.Web.Features.Localization;

namespace AniLingo.Web.Ui;

public sealed record MetricCardModel(string Label, string Value, string? Hint = null);

/// <summary>
/// <paramref name="Ui"/> only backs the fallback aria-label used when a caller
/// omits <paramref name="ProgressLabel"/>; every current caller supplies its
/// own localized <see cref="ProgressLabel"/>, so it defaults to null.
/// </summary>
public sealed record MediaCardModel(
    string Title,
    string Subtitle,
    string Href,
    int? ProgressPercent = null,
    string? Badge = null,
    string? ImageUrl = null,
    string? ProgressLabel = null,
    UiTextBundle? Ui = null);
