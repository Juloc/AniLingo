using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Sonarr;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

/// <summary>
/// Closes the #301 "different mount paths" gap: the same canonical remote path mapping used for
/// completed-download paths also translates Sonarr-observed paths, so path comparisons in
/// SonarrOwnershipRecognizer/SonarrParallelSafety compare AniLingo's own view of a path.
/// </summary>
[TestClass]
public sealed class SonarrPathMappingTests
{
    [TestMethod]
    public async Task SonarrObservedPathsAreTranslatedThroughTheRemotePathMapping()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anilingo-sonarr-mapping-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var importSettings = new AnimeImportSettingsStore(root);
            await importSettings.UpdateAsync(state => state with
            {
                RemotePathMappings = [new RemotePathMapping("/tv", "/data/anime")]
            });

            var ownership = new AcquisitionOwnershipStore(root);
            var client = new FakeSonarrObserverClient(
                series: [new SonarrObservedSeries(1, "Frieren", "/tv/anime/Frieren", true)],
                files: [new SonarrObservedEpisodeFile(11, 1, 1, "/tv/anime/Frieren/Season 01/Frieren - S01E01.mkv")],
                queue: [new SonarrObservedQueueItem(101, 1, "Frieren - S01E02", "release-key", "nzo1", "/tv/downloads/Frieren.S01E02", "downloading", "downloading", null)],
                history: [new SonarrObservedHistoryEvent(9001, 1, SonarrHistoryEventKind.Imported, "downloadFolderImported", DateTimeOffset.UtcNow, "Frieren - S01E01", null, "nzo0", "/tv/downloads/Frieren.S01E01", "/tv/anime/Frieren/Season 01/Frieren - S01E01.mkv", null)]);

            var connectionStore = new SonarrConnectionStore(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
                new DirectoryInfo(Path.Combine(root, "sonarr-import")));
            await connectionStore.SaveAsync(new SonarrConnectionSettings("http://sonarr:8989", "key"), CancellationToken.None);

            // Link "frieren" to Sonarr series 1 in a non-read-only mode so episode files are fetched.
            await ownership.UpdateAsync(state =>
            {
                var anime = new Dictionary<string, AnimeManagementAssignment>(state.Anime, StringComparer.OrdinalIgnoreCase)
                {
                    ["frieren"] = new("frieren", AnimeManagementMode.ParallelAcquisition, DateTimeOffset.UtcNow, SonarrSeriesId: 1)
                };
                return state with { Anime = anime };
            });

            var observation = new SonarrObservationService(
                connectionStore,
                client,
                ownership,
                importSettings,
                NullLogger<SonarrObservationService>.Instance);

            var state = await observation.GetObservationAsync(await ownership.LoadAsync(), forceRefresh: true, CancellationToken.None);

            // The stored observed state keeps the mapping's raw output; comparisons elsewhere
            // (SonarrOwnershipRecognizer.RecognizePath below) normalize both sides through the
            // OS path rules at comparison time.
            Assert.AreEqual("/data/anime/anime/Frieren", state.Series.Single().Path);
            Assert.AreEqual("/data/anime/anime/Frieren/Season 01/Frieren - S01E01.mkv", state.EpisodeFiles.Single().Path);
            Assert.AreEqual("/data/anime/downloads/Frieren.S01E02", state.Queue.Single().OutputPath);
            Assert.AreEqual("/data/anime/downloads/Frieren.S01E01", state.History.Single().SourcePath);
            Assert.AreEqual("/data/anime/anime/Frieren/Season 01/Frieren - S01E01.mkv", state.History.Single().Path);

            // The translated path is what recognizes an AniLingo-local path as Sonarr-owned.
            var recognized = SonarrOwnershipRecognizer.RecognizePath(state, Path.GetFullPath("/data/anime/anime/Frieren/Season 01/Frieren - S01E01.mkv"));
            Assert.AreEqual(SonarrPathOwnershipKind.EpisodeFile, recognized.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeSonarrObserverClient(
        IReadOnlyList<SonarrObservedSeries> series,
        IReadOnlyList<SonarrObservedEpisodeFile> files,
        IReadOnlyList<SonarrObservedQueueItem> queue,
        IReadOnlyList<SonarrObservedHistoryEvent> history) : ISonarrObserverClient
    {
        public Task<IReadOnlyList<SonarrObservedSeries>> GetSeriesAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(series);

        public Task<IReadOnlyList<SonarrObservedEpisodeFile>> GetEpisodeFilesAsync(SonarrConnectionSettings settings, int seriesId, CancellationToken cancellationToken) =>
            Task.FromResult(files);

        public Task<IReadOnlyList<SonarrObservedQueueItem>> GetQueueAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(queue);

        public Task<IReadOnlyList<SonarrObservedHistoryEvent>> GetRecentHistoryAsync(SonarrConnectionSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(history);
    }
}
