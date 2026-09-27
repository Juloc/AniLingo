using Jularr.Web.Features.ReaderPreferences;

namespace Jularr.Web.Features.ReaderCore;

/// <param name="LayoutPreferences">
/// Renders the layout preferences (hyphenation, paragraph indent, page numbers,
/// illustrations, automatic chapter continue). Only readers that implement them
/// pass true, so no reader shows a control it ignores.
/// </param>
public sealed record ReaderSettingsPanelModel(
    Guid ChapterId,
    ReaderDocumentDescriptor Document,
    ReaderSettingsSnapshot Settings,
    string? Language = null,
    string? AdditionalActionUrl = null,
    string? AdditionalActionLabel = null,
    bool LayoutPreferences = false);
