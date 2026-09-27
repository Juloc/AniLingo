using Jularr.Web.Features.Acquisition.Ownership;

namespace Jularr.Tests;

[TestClass]
public sealed class SonarrParallelSafetyTests
{
    [TestMethod]
    public void ReadOnlyCoexistenceNeverAllowsJularrGrab()
    {
        var decision = SonarrParallelSafety.CanGrab(
            AcquisitionOwnershipState.Empty(),
            SonarrObservedState.Empty,
            "anime",
            "release");

        Assert.IsFalse(decision.Allowed);
    }

    [TestMethod]
    public void ParallelModeRejectsReleaseAlreadyActiveInSonarr()
    {
        var state = SonarrParallelSafety.SetMode(
            AcquisitionOwnershipState.Empty(),
            "anime",
            AnimeManagementMode.ParallelAcquisition,
            DateTimeOffset.UtcNow);
        var sonarr = new SonarrObservedState(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "release" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var decision = SonarrParallelSafety.CanGrab(state, sonarr, "anime", "RELEASE");

        Assert.IsFalse(decision.Allowed);
    }

    [TestMethod]
    public void DifferentAnimeCanUseDifferentManagers()
    {
        var now = DateTimeOffset.UtcNow;
        var state = SonarrParallelSafety.SetMode(
            AcquisitionOwnershipState.Empty(),
            "sonarr-anime",
            AnimeManagementMode.ReadOnlyCoexistence,
            now);
        state = SonarrParallelSafety.SetMode(
            state,
            "jularr-anime",
            AnimeManagementMode.JularrManaged,
            now);

        Assert.AreEqual(
            AnimeManagementMode.ReadOnlyCoexistence,
            SonarrParallelSafety.GetMode(state, "sonarr-anime"));
        Assert.AreEqual(
            AnimeManagementMode.JularrManaged,
            SonarrParallelSafety.GetMode(state, "jularr-anime"));
    }

    [TestMethod]
    public void ParallelModeCanOnlyMutateExplicitJularrOwnedPath()
    {
        var now = DateTimeOffset.UtcNow;
        var state = SonarrParallelSafety.SetMode(
            AcquisitionOwnershipState.Empty(),
            "anime",
            AnimeManagementMode.ParallelAcquisition,
            now);

        var unowned = SonarrParallelSafety.CanMutatePath(
            state,
            SonarrObservedState.Empty,
            "anime",
            "/library/anime/episode.mkv");
        Assert.IsFalse(unowned.Allowed);

        state = SonarrParallelSafety.RegisterPath(
            state,
            new ManagedMediaPath(
                "/library/anime/episode.mkv",
                "anime",
                AcquisitionOwner.Jularr,
                "job-1",
                now));

        var owned = SonarrParallelSafety.CanMutatePath(
            state,
            SonarrObservedState.Empty,
            "anime",
            "/library/anime/episode.mkv");
        Assert.IsTrue(owned.Allowed);
    }

    [TestMethod]
    public void CannotHandBackToSonarrWhileJularrJobIsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var state = SonarrParallelSafety.SetMode(
            AcquisitionOwnershipState.Empty(),
            "anime",
            AnimeManagementMode.JularrManaged,
            now);
        state = SonarrParallelSafety.RegisterJob(
            state,
            new AcquisitionOwnership(
                "job-1",
                "anime",
                AcquisitionOwner.Jularr,
                "release",
                AcquisitionOwnershipStatus.Importing,
                now));

        var decision = SonarrParallelSafety.CanChangeMode(
            state,
            "anime",
            AnimeManagementMode.ReadOnlyCoexistence);

        Assert.IsFalse(decision.Allowed);
    }

    [TestMethod]
    public void DetectsReleaseAndPathConflicts()
    {
        var now = DateTimeOffset.UtcNow;
        var state = SonarrParallelSafety.SetMode(
            AcquisitionOwnershipState.Empty(),
            "anime",
            AnimeManagementMode.ParallelAcquisition,
            now);
        state = SonarrParallelSafety.RegisterJob(
            state,
            new AcquisitionOwnership(
                "job",
                "anime",
                AcquisitionOwner.Jularr,
                "release",
                AcquisitionOwnershipStatus.Pending,
                now));
        state = SonarrParallelSafety.RegisterPath(
            state,
            new ManagedMediaPath(
                "/library/anime/e01.mkv",
                "anime",
                AcquisitionOwner.Jularr,
                "job",
                now));

        var sonarr = new SonarrObservedState(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "release" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/library/anime/e01.mkv" });

        var conflicts = SonarrParallelSafety.DetectConflicts(state, sonarr);

        Assert.AreEqual(2, conflicts.Count);
    }

    [TestMethod]
    public async Task OwnershipStateSurvivesRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "jularr-ownership-" + Guid.NewGuid());
        try
        {
            var now = DateTimeOffset.UtcNow;
            var state = SonarrParallelSafety.SetMode(
                AcquisitionOwnershipState.Empty(),
                "anime",
                AnimeManagementMode.JularrManaged,
                now);
            state = SonarrParallelSafety.RegisterJob(
                state,
                new AcquisitionOwnership(
                    "job",
                    "anime",
                    AcquisitionOwner.Jularr,
                    "release",
                    AcquisitionOwnershipStatus.Completed,
                    now));

            await new AcquisitionOwnershipStore(root).SaveAsync(state);
            var loaded = await new AcquisitionOwnershipStore(root).LoadAsync();

            Assert.AreEqual(AnimeManagementMode.JularrManaged, loaded.Anime["anime"].Mode);
            Assert.AreEqual(AcquisitionOwner.Jularr, loaded.Jobs["job"].Owner);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
