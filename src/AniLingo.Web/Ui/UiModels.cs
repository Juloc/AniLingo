namespace AniLingo.Web.Ui;

public sealed record MetricCardModel(string Label, string Value, string? Hint = null);

public sealed record MediaCardModel(
    string Title,
    string Subtitle,
    string Href,
    int? ProgressPercent = null,
    string? Badge = null,
    string? ImageUrl = null,
    string? ProgressLabel = null);
