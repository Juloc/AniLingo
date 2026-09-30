using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Localization;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaKindLabelKeysTests
{
    [TestMethod]
    public void EveryMediaKindHasALabelKeyInEveryKindLabelMapping()
    {
        var mappings = new (string Name, Func<MediaAcquisitionKind, string> Key)[]
        {
            ("name", MediaKindLabelKeys.Name),
            ("operation", MediaKindLabelKeys.Operation),
            ("folders", MediaKindLabelKeys.Folders),
            ("clientCategory", MediaKindLabelKeys.ClientCategory)
        };

        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            foreach (var (name, key) in mappings)
            {
                Assert.IsTrue(
                    UiTranslationResources.TryGet(key(kind), out _),
                    $"{name} label key '{key(kind)}' for {kind} is missing from the UI catalog.");
            }

            var inboxLabel = MediaInboxImportService.Label(kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(inboxLabel), $"Inbox label for {kind} is empty.");
        }
    }
}
