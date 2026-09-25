using AniLingo.Web.Features.ReaderPreferences;

namespace AniLingo.Web.Features.ReaderCore;

public sealed record ReaderSettingsPanelModel(
    Guid ChapterId,
    ReaderDocumentDescriptor Document,
    ReaderSettingsSnapshot Settings,
    string? Language = null,
    string? AdditionalActionUrl = null,
    string? AdditionalActionLabel = null);
