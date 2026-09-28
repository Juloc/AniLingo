namespace Jularr.Web.Ui;

/// <summary>
/// A visible back action that uses same-origin browser history when available
/// and otherwise keeps a deterministic route for direct entries.
/// </summary>
public sealed record ContextBackLinkModel(
    string FallbackHref,
    string Label,
    string CssClass);
