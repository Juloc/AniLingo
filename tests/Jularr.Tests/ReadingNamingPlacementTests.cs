using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Naming;
using Jularr.Web.Features.ReadingAcquisition;

namespace Jularr.Tests;

// Verifies naming profiles (#529) apply where the #389 import pipeline places reading releases
// into their NAS library root: MangaLibraryPlacement (Manga) and ReadingLibraryPlacement
// (Books/Light Novels), both in Features/ReadingAcquisition/ReadingCompletedDownloadAdapters.cs.
[TestClass]
public sealed class ReadingNamingPlacementTests
{
    [TestMethod]
    public void WithoutAProfileMangaPlacementIsUnchanged()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-place-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "source.cbz");
            File.WriteAllText(sourceFile, "cbz");
            var placement = new MangaLibraryPlacement(new ImportFileTransfer(new FileSystemHardLinkCreator()));

            var seriesFolder = MangaLibraryPlacement.SeriesFolder(directory.FullName, existing: null, "My: Manga");
            Assert.AreEqual(Path.Combine(Path.GetFullPath(directory.FullName), MangaLibraryPlacement.SafeName("My: Manga")), seriesFolder);

            var releaseTarget = Path.Combine(seriesFolder, MangaLibraryPlacement.SafeName("chapter 001.cbz"));
            var placed = placement.Place(sourceFile, releaseTarget, ImportMode.Copy);

            Assert.AreEqual(1, placed);
            Assert.IsTrue(File.Exists(releaseTarget));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void CustomMangaProfileRenamesTheChapterFileFromItsOwnName()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-place-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "Sample Manga - c012.5.cbz");
            File.WriteAllText(sourceFile, "cbz");
            var placement = new MangaLibraryPlacement(new ImportFileTransfer(new FileSystemHardLinkCreator()));
            var profile = ReadingNamingPresets.Structured(MediaAcquisitionKind.Manga);

            var seriesFolder = MangaLibraryPlacement.SeriesFolder(directory.FullName, existing: null, "Sample Manga", profile);
            // For a single-file source, the caller renders the final leaf name itself (so the
            // same rendered path is used for retry/idempotency checks, not just placement) -
            // mirrors what MangaCompletedDownloadImportAdapter.ImportAsync does.
            var releaseTarget = Path.Combine(
                seriesFolder,
                ReadingNamingPlacement.RenderChapterLeafName(
                    sourceFile,
                    profile,
                    "Sample Manga",
                    MangaLibraryPlacement.SafeName(Path.GetFileName(sourceFile))));

            placement.Place(sourceFile, releaseTarget, ImportMode.Copy, profile, "Sample Manga");

            var expected = Path.Combine(seriesFolder, "Sample Manga - Chapter 012.5.cbz");
            Assert.IsTrue(File.Exists(expected), $"Expected renamed chapter file at '{expected}'.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void CustomProfileRenamesAnEpubReleaseFolderAndFile()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-place-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "some-release.epub");
            File.WriteAllText(sourceFile, "epub");
            var placement = new ReadingLibraryPlacement(new ImportFileTransfer(new FileSystemHardLinkCreator()));
            var profile = new ReadingNamingProfile(MediaAcquisitionKind.LightNovel, "Custom", "{Series}", "{Series} [{Format}]");

            var destination = ReadingLibraryPlacement.ReleaseFolder(directory.FullName, "Sample Chronicles", profile);
            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(directory.FullName), "Sample Chronicles"),
                destination);

            placement.PlaceEpubs(sourceFile, destination, ImportMode.Copy, profile, "Sample Chronicles");

            var expected = Path.Combine(destination, "Sample Chronicles [EPUB].epub");
            Assert.IsTrue(File.Exists(expected), $"Expected renamed EPUB at '{expected}'.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void WithoutAProfileBookFilesKeepTheirOriginalNameLikeBeforeNamingProfilesExisted()
    {
        var directory = Directory.CreateTempSubdirectory("jularr-reading-naming-place-");
        try
        {
            var sourceFile = Path.Combine(directory.FullName, "My Book.pdf");
            File.WriteAllText(sourceFile, "pdf");
            var placement = new ReadingLibraryPlacement(new ImportFileTransfer(new FileSystemHardLinkCreator()));

            var destination = ReadingLibraryPlacement.ReleaseFolder(directory.FullName, "My Book");
            placement.PlaceBookFiles(sourceFile, destination, ImportMode.Copy);

            Assert.IsTrue(File.Exists(Path.Combine(destination, "My Book.pdf")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
