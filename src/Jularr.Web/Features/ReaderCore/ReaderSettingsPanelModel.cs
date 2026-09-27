using Jularr.Web.Features.ReaderPreferences;

namespace Jularr.Web.Features.ReaderCore;

public sealed record ReaderSettingsPanelModel(
    Guid ChapterId,
    ReaderDocumentDescriptor Document,
    ReaderSettingsSnapshot Settings,
    string? Language = null,
    string? AdditionalActionUrl = null,
    string? AdditionalActionLabel = null);
