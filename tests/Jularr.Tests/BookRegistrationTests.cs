using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Quality;
using Jularr.Web.Features.Acquisition.Release;

namespace Jularr.Tests;

[TestClass]
public sealed class BookRegistrationTests
{
    private static MediaAcquisitionRegistry Registry() =>
        new([
            new AnimeAcquisitionRegistration(),
            new BookAcquisitionRegistration()
        ]);

    [TestMethod]
    public void RegistryResolvesBookParserProfileAndItemMonitoring()
    {
        var registry = Registry();

        Assert.IsTrue(registry.Supports(MediaAcquisitionKind.Book));
        Assert.AreEqual(
            BookQualityProfiles.DefaultBookId,
            registry.DefaultProfileFor(MediaAcquisitionKind.Book).Id);
        Assert.AreEqual(
            MonitoringGranularity.Item,
            registry.MonitoringGranularityFor(MediaAcquisitionKind.Book));
        Assert.AreEqual(
            0,
            ReleaseScorer.ValidateProfile(
                registry.DefaultProfileFor(MediaAcquisitionKind.Book)).Count);
    }

    [TestMethod]
    public void BookParserExposesDocumentFormatAsSharedQuality()
    {
        var parser = BookReleaseParser.Instance;

        var epub = parser.Parse("Frank Herbert - Dune retail EPUB");
        var pdf = parser.Parse("Frank Herbert - Dune.pdf");
        var unknown = parser.Parse("Frank Herbert - Dune");

        Assert.AreEqual("EPUB", epub.DocumentFormat);
        Assert.AreEqual("EPUB", ReleaseQuality.GetKey(epub));
        Assert.AreEqual("PDF", pdf.DocumentFormat);
        Assert.AreEqual("PDF", ReleaseQuality.GetKey(pdf));
        Assert.IsNull(unknown.DocumentFormat);
        Assert.AreEqual("UNKNOWN-UNKNOWN", ReleaseQuality.GetKey(unknown));
    }

    [TestMethod]
    public void DefaultBookProfilePrefersEpubAcceptsPdfAndRejectsUnsupportedFormat()
    {
        var profile = BookQualityProfiles.CreateDefaultBook();

        ReleaseScoreResult Score(string title) =>
            ReleaseScorer.Score(
                profile,
                new ReleaseCandidate(
                    BookReleaseParser.Instance.Parse(title),
                    4_000_000));

        var ranked = ReleaseScorer.Rank(
            profile,
            [
                new ReleaseCandidate(BookReleaseParser.Instance.Parse("Dune PDF"), 4_000_000),
                new ReleaseCandidate(BookReleaseParser.Instance.Parse("Dune retail EPUB"), 4_000_000),
                new ReleaseCandidate(BookReleaseParser.Instance.Parse("Dune"), 4_000_000)
            ]);

        Assert.AreEqual("EPUB", ranked[0].QualityKey);
        Assert.IsTrue(ranked[0].Accepted);
        Assert.AreEqual(2, ranked[0].Score);
        Assert.IsTrue(Score("Dune PDF").Accepted);
        Assert.IsTrue(Score("Dune").Accepted);

        var mobi = Score("Dune MOBI");
        Assert.IsFalse(mobi.Accepted);
        CollectionAssert.Contains(
            mobi.RejectionReasons.ToList(),
            "Quality 'MOBI' is not allowed.");
    }
}
