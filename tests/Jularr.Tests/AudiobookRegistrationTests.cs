using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Release;
using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

/// <summary>
/// Audiobook (#440) as a first-class acquisition kind: its name mappings round-trip, it maps onto the
/// Book capability matrix, and it registers a parser + default quality profile on the shared engine so
/// the registry never throws.
/// </summary>
[TestClass]
public sealed class AudiobookRegistrationTests
{
    private static MediaAcquisitionRegistry Registry() =>
        new([
            new AnimeAcquisitionRegistration(),
            new AudiobookAcquisitionRegistration()
        ]);

    [TestMethod]
    public void KindNameRoundTripsForAudiobook()
    {
        Assert.AreEqual("audiobook", AcquisitionAccessNames.Kind(MediaAcquisitionKind.Audiobook));
        Assert.AreEqual(MediaAcquisitionKind.Audiobook, AcquisitionAccessNames.ParseKind("audiobook"));
    }

    [TestMethod]
    public void WorkTypeMapsAudiobookToBook()
    {
        // An audiobook is an audio edition of a book, so it shares the Book capability matrix and the
        // media core represents it as a Book Work.
        Assert.AreEqual(WorkMediaType.Book, AcquisitionAccessNames.WorkType(MediaAcquisitionKind.Audiobook));
    }

    [TestMethod]
    public void EveryKindHasAWorkTypeAndAName()
    {
        // Guards against a new MediaAcquisitionKind slipping in without the central mappings that
        // GetCapabilitiesAsync and the acquisition stores rely on.
        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            var name = AcquisitionAccessNames.Kind(kind);
            Assert.AreEqual(kind, AcquisitionAccessNames.ParseKind(name));
            _ = AcquisitionAccessNames.WorkType(kind);
        }
    }

    [TestMethod]
    public void RegistryResolvesParserAndDefaultProfileForAudiobook()
    {
        var registry = Registry();

        Assert.IsTrue(registry.Supports(MediaAcquisitionKind.Audiobook));
        Assert.IsNotNull(registry.ParserFor(MediaAcquisitionKind.Audiobook));
        Assert.AreEqual(
            AudiobookQualityProfiles.DefaultAudiobookId,
            registry.DefaultProfileFor(MediaAcquisitionKind.Audiobook).Id);
    }
}
