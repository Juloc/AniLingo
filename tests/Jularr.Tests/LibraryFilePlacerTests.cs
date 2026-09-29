using Jularr.Web.Features.Acquisition.Import;

namespace Jularr.Tests;

/// <summary>
/// The shared file commit and download-file scan behind the Anime importer (#389): transfer with
/// the owner's import mode, sidecars follow, replaced files go only after the new file is in place.
/// </summary>
[TestClass]
public sealed class LibraryFilePlacerTests
{
    [TestMethod]
    public void PlacesTheFileMovesSidecarsAndDeletesReplacedFilesAfterTheCommit()
    {
        using var directory = new TempDirectory();
        var source = directory.File("download", "Frieren.S01E02.mkv", "new video");
        var sidecar = directory.File("download", "Frieren.S01E02.ja.srt", "subtitle");
        var replaced = directory.File("library", "Season 01", "Frieren - S01E02 - old.mkv", "old video");
        var destination = Path.Combine(directory.Path, "library", "Season 01", "Frieren - S01E02 - Episode 2.mkv");

        var notes = Placer().Place(new LibraryFilePlacement(
            source,
            destination,
            ImportFileAction.Move,
            AllowHardlinkFallbackToCopy: false,
            [new PlacedSidecar(sidecar, "Frieren - S01E02 - Episode 2.ja.srt")],
            [replaced]));

        Assert.AreEqual(0, notes.Count);
        Assert.AreEqual("new video", File.ReadAllText(destination));
        Assert.IsFalse(File.Exists(source), "Move takes the source.");
        Assert.AreEqual("subtitle", File.ReadAllText(Path.Combine(Path.GetDirectoryName(destination)!, "Frieren - S01E02 - Episode 2.ja.srt")));
        Assert.IsFalse(File.Exists(sidecar));
        Assert.IsFalse(File.Exists(replaced), "The replaced file goes once the new one is in place.");
    }

    [TestMethod]
    public void AFailedTransferKeepsTheSourceAndTheFileItWouldHaveReplaced()
    {
        using var directory = new TempDirectory();
        var source = directory.File("download", "Frieren.S01E02.mkv", "new video");
        var replaced = directory.File("library", "old.mkv", "old video");
        var destination = Path.Combine(directory.Path, "library", "new.mkv");

        var placer = new LibraryFilePlacer(new ImportFileTransfer(new CrossDeviceHardLinkCreator()));
        Assert.ThrowsExactly<CrossDeviceLinkException>(() => placer.Place(new LibraryFilePlacement(
            source,
            destination,
            ImportFileAction.Hardlink,
            AllowHardlinkFallbackToCopy: false,
            [],
            [replaced])));

        Assert.IsTrue(File.Exists(source));
        Assert.AreEqual("old video", File.ReadAllText(replaced), "An upgrade never loses the existing file when the new one did not arrive.");
        Assert.IsFalse(File.Exists(destination));
    }

    [TestMethod]
    public void HardlinkOrCopyCopiesAcrossFilesystemsAndLeavesTheSource()
    {
        using var directory = new TempDirectory();
        var source = directory.File("download", "a.mkv", "video");
        var destination = Path.Combine(directory.Path, "library", "a.mkv");

        var placer = new LibraryFilePlacer(new ImportFileTransfer(new CrossDeviceHardLinkCreator()));
        placer.Place(new LibraryFilePlacement(source, destination, ImportFileAction.Hardlink, AllowHardlinkFallbackToCopy: true, [], []));

        Assert.AreEqual("video", File.ReadAllText(destination));
        Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public void ASidecarThatCannotMoveIsANoteNotAFailure()
    {
        using var directory = new TempDirectory();
        var source = directory.File("download", "a.mkv", "video");
        var sidecar = directory.File("download", "a.srt", "subtitle");
        var destination = Path.Combine(directory.Path, "library", "a.mkv");
        // A folder where the sidecar has to go.
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(destination)!, "a.ja.srt"));

        var notes = Placer().Place(new LibraryFilePlacement(
            source,
            destination,
            ImportFileAction.Move,
            AllowHardlinkFallbackToCopy: false,
            [new PlacedSidecar(sidecar, "a.ja.srt")],
            []));

        Assert.IsTrue(File.Exists(destination), "The video is in the library regardless.");
        Assert.AreEqual(1, notes.Count);
        StringAssert.Contains(notes[0], "Sidecar a.srt stayed in the download folder");
        Assert.IsTrue(File.Exists(sidecar));
    }

    [TestMethod]
    public void AnExistingSidecarIsNeverOverwritten()
    {
        using var directory = new TempDirectory();
        var source = directory.File("download", "a.mkv", "video");
        var sidecar = directory.File("download", "a.srt", "new subtitle");
        var existing = directory.File("library", "a.ja.srt", "kept subtitle");

        var notes = Placer().Place(new LibraryFilePlacement(
            source,
            Path.Combine(directory.Path, "library", "a.mkv"),
            ImportFileAction.Move,
            AllowHardlinkFallbackToCopy: false,
            [new PlacedSidecar(sidecar, "a.ja.srt")],
            []));

        Assert.AreEqual(0, notes.Count);
        Assert.AreEqual("kept subtitle", File.ReadAllText(existing));
        Assert.IsTrue(File.Exists(sidecar));
    }

    [TestMethod]
    public void ADestinationIsOnlyFreeOrOneOfTheFilesTheImportReplaces()
    {
        using var directory = new TempDirectory();
        var existing = directory.File("library", "Episode.mkv", "someone else's file");

        Assert.IsNull(LibraryFilePlacer.FindDestinationConflict(Path.Combine(directory.Path, "library", "free.mkv"), []));
        StringAssert.Contains(LibraryFilePlacer.FindDestinationConflict(existing, []), "Destination already exists: Episode.mkv");
        StringAssert.Contains(LibraryFilePlacer.FindDestinationConflict(existing, [existing.ToUpperInvariant() + "x"]), "already exists");
        Assert.IsNull(LibraryFilePlacer.FindDestinationConflict(existing, [existing.ToUpperInvariant()]), "Case does not make it another file.");
        Assert.IsNull(LibraryFilePlacer.FindSourceProblem(existing));
        Assert.AreEqual("The downloaded file no longer exists.", LibraryFilePlacer.FindSourceProblem(existing + ".gone"));
    }

    [TestMethod]
    public void SamePathIgnoresCaseAndTrailingSeparators()
    {
        Assert.IsTrue(LibraryFilePlacer.SamePath("/data/Anime/", "/data/anime"));
        Assert.IsFalse(LibraryFilePlacer.SamePath("/data/anime", "/data/anime2"));
        Assert.IsFalse(LibraryFilePlacer.SamePath(null, "/data/anime"));
    }

    [TestMethod]
    public void DownloadFilesAreListedInAStableOrderAndClassified()
    {
        using var directory = new TempDirectory();
        directory.File("job", "B.mkv", "b");
        directory.File("job", "A.mkv", "a");
        directory.File("job", "Sub", "A.ja.srt", "s");
        directory.File("job", "job.nfo", "n");
        directory.File("job", "poster.jpg", "p");
        directory.File("job", "job.par2", "x");
        directory.File("job", "notes.pdf", "pdf");

        var files = CompletedDownloadFiles.Enumerate(Path.Combine(directory.Path, "job"), out var error);

        Assert.IsNull(error);
        CollectionAssert.AreEqual(
            files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray(),
            files.Select(file => file.Path).ToArray(),
            "The listing is ordered by path.");
        Assert.AreEqual(7, files.Count);
        Assert.IsTrue(CompletedDownloadFiles.IsVideo("x.MKV"));
        Assert.IsFalse(CompletedDownloadFiles.IsVideo("x.srt"));
        Assert.IsTrue(CompletedDownloadFiles.IsSidecar("x.ja.ass"));
        Assert.IsTrue(CompletedDownloadFiles.IsSidecarOrJunk("job.par2"));
        Assert.IsTrue(CompletedDownloadFiles.IsSidecarOrJunk("poster.jpg"));
        Assert.IsFalse(CompletedDownloadFiles.IsSidecarOrJunk("notes.pdf"), "An unknown file type is worth reporting when ignored.");
        Assert.IsFalse(CompletedDownloadFiles.IsSidecarOrJunk("x.mkv"));
    }

    [TestMethod]
    public void ASingleFileDownloadIsItsOwnListingAndAMissingPathSaysSo()
    {
        using var directory = new TempDirectory();
        var single = directory.File("job", "a.mkv", "abc");

        var files = CompletedDownloadFiles.Enumerate(single, out var error);
        Assert.IsNull(error);
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(3, files[0].SizeBytes);

        var missing = CompletedDownloadFiles.Enumerate(Path.Combine(directory.Path, "gone"), out var missingError);
        Assert.AreEqual(0, missing.Count);
        StringAssert.Contains(missingError, "does not exist or is not mounted");
    }

    private static LibraryFilePlacer Placer() =>
        new(new ImportFileTransfer(new FileSystemHardLinkCreator()));

    private sealed class CrossDeviceHardLinkCreator : IHardLinkCreator
    {
        public void CreateHardLink(string sourcePath, string destinationPath) =>
            throw new CrossDeviceLinkException(sourcePath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jularr-placer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        /// <summary>Writes a file below the temp folder and returns its full path.</summary>
        public string File(params string[] partsAndContent)
        {
            var parts = partsAndContent[..^1];
            var full = System.IO.Path.Combine([Path, .. parts]);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, partsAndContent[^1]);
            return full;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
