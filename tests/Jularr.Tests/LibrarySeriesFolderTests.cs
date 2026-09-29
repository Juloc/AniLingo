using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Artwork;

namespace Jularr.Tests;

// Issue #581: a Light Novel or Manga series keeps its artwork in its series folder -- the first
// folder below the media type's library root -- on the shared per-kind import policy.
[TestClass]
public sealed class LibrarySeriesFolderTests
{
    [TestMethod]
    public void FileDeepInsideASeriesResolvesToTheFirstFolderBelowTheRoot()
    {
        using var library = new TempLibrary();
        var volume = library.MakeFile("Frieren", "Vol 01", "frieren-1.epub");

        Assert.AreEqual(library.PathOf("Frieren"), LibrarySeriesFolder.Resolve(library.Root, volume));
    }

    [TestMethod]
    public void TheSeriesFolderItselfResolvesToItself()
    {
        using var library = new TempLibrary();
        var series = library.MakeDirectory("Frieren");

        Assert.AreEqual(series, LibrarySeriesFolder.Resolve(library.Root + Path.DirectorySeparatorChar, series));
    }

    [TestMethod]
    public void MediaDirectlyInTheRootHasNoSeriesFolder()
    {
        using var library = new TempLibrary();
        var loose = library.MakeFile("loose.epub");

        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, loose), "A shared root would make every series fight over one cover.");
        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, library.Root));
    }

    [TestMethod]
    public void PathsOutsideTheRootAreNeverResolved()
    {
        using var library = new TempLibrary();
        var sibling = library.MakeSibling("light-novels-archive");
        var siblingVolume = Path.Combine(Directory.CreateDirectory(Path.Combine(sibling, "Frieren")).FullName, "v1.epub");
        File.WriteAllBytes(siblingVolume, [1]);
        var traversal = Path.Combine(library.Root, "..", "light-novels-archive", "Frieren", "v1.epub");

        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, siblingVolume), "A sibling sharing the root's name prefix is not below it.");
        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, traversal), ".. segments cannot climb out of the root.");
    }

    [TestMethod]
    public void MissingFolderMeansUnavailableStorage()
    {
        using var library = new TempLibrary();
        var gone = Path.Combine(library.Root, "Frieren", "v1.epub");

        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, gone));
        Assert.IsNull(LibrarySeriesFolder.Resolve(null, gone));
        Assert.IsNull(LibrarySeriesFolder.Resolve(library.Root, "  "));
    }

    [TestMethod]
    public void NoConfiguredLibraryRootResolvesNothing()
    {
        using var library = new TempLibrary();
        var volume = library.MakeFile("Frieren", "v1.epub");

        Assert.IsNull(LibrarySeriesFolder.Resolve(AnimeImportSettingsState.Empty(), MediaAcquisitionKind.LightNovel, volume));
    }

    [TestMethod]
    public void StoredPathInTheReportedFormIsTranslatedThroughTheKindsRemoteMappings()
    {
        using var library = new TempLibrary();
        var series = library.MakeDirectory("Frieren");
        var settings = AnimeImportSettingsState.Empty() with
        {
            MediaLibraries = new Dictionary<MediaAcquisitionKind, MediaLibraryTarget>
            {
                [MediaAcquisitionKind.Manga] = new MediaLibraryTarget(LibraryRoot: library.Root)
                {
                    RemotePathMappings = [new RemotePathMapping("/remote/manga", library.Root)]
                }
            }
        };

        Assert.AreEqual(series, LibrarySeriesFolder.Resolve(settings, MediaAcquisitionKind.Manga, "/remote/manga/Frieren/Vol 01.cbz"));
        Assert.AreEqual(
            series,
            LibrarySeriesFolder.Resolve(settings, MediaAcquisitionKind.Manga, Path.Combine(series, "Vol 01.cbz")),
            "A path already below the root is used as is.");
        Assert.IsNull(
            LibrarySeriesFolder.Resolve(settings, MediaAcquisitionKind.LightNovel, "/remote/manga/Frieren/Vol 01.cbz"),
            "Mappings and library roots belong to one media type.");
    }

    private sealed class TempLibrary : IDisposable
    {
        private readonly string parent = Path.Combine(Path.GetTempPath(), $"jularr-series-folder-{Guid.NewGuid():N}");

        public TempLibrary()
        {
            Root = Path.Combine(parent, "light-novels");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(string name) => Path.Combine(Root, name);

        public string MakeDirectory(string name) => Directory.CreateDirectory(PathOf(name)).FullName;

        public string MakeFile(params string[] segments)
        {
            var path = Path.Combine([Root, .. segments]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
            return path;
        }

        public string MakeSibling(string name) => Directory.CreateDirectory(Path.Combine(parent, name)).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
