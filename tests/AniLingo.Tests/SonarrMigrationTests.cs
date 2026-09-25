using AniLingo.Web.Features.Acquisition.Ownership;

namespace AniLingo.Tests;

[TestClass]
public sealed class SonarrMigrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void KeepSonarrIsIdempotentAndNeverTouchesSonarr()
    {
        var sonarr = Observed(monitored: true);
        var request = new AnimeMigrationRequest("frieren", AnimeMigrationAction.KeepSonarr, 1, ApplySonarrMonitoring: true);

        var first = SonarrMigration.Plan(AcquisitionOwnershipState.Empty(), sonarr, request, Now);
        var second = SonarrMigration.Plan(first.State, sonarr, request, Now.AddMinutes(1));

        Assert.IsTrue(first.Allowed);
        Assert.IsTrue(first.Changed);
        Assert.IsNull(first.SetSonarrMonitored);
        Assert.AreEqual(AnimeManagementMode.ReadOnlyCoexistence, first.State.Anime["frieren"].Mode);
        Assert.AreEqual(1, first.State.Anime["frieren"].SonarrSeriesId);

        Assert.IsTrue(second.Allowed);
        Assert.IsFalse(second.Changed);
        Assert.AreSame(first.State, second.State);
        Assert.AreEqual(1, second.State.MigrationLog.Count);
    }

    [TestMethod]
    public void HandOverUnmonitorsOnlyWhenRequestedAndIsIdempotent()
    {
        var request = new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1, "Frieren", ApplySonarrMonitoring: true);

        var first = SonarrMigration.Plan(AcquisitionOwnershipState.Empty(), Observed(monitored: true), request, Now);

        Assert.IsTrue(first.Allowed);
        Assert.IsFalse(first.SetSonarrMonitored);
        Assert.AreEqual(1, first.SonarrSeriesId);
        var assignment = first.State.Anime["frieren"];
        Assert.AreEqual(AnimeManagementMode.AniLingoManaged, assignment.Mode);
        Assert.IsTrue(assignment.SonarrUnmonitoredByAniLingo);

        // Sonarr now reports the series unmonitored; repeating the action changes nothing.
        var second = SonarrMigration.Plan(first.State, Observed(monitored: false), request, Now.AddMinutes(1));
        Assert.IsTrue(second.Allowed);
        Assert.IsFalse(second.Changed);
        Assert.IsNull(second.SetSonarrMonitored);
        Assert.AreEqual(1, second.State.MigrationLog.Count);
    }

    [TestMethod]
    public void HandOverWithoutMonitoringOptionLeavesSonarrUntouchedAndWarns()
    {
        var plan = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            Observed(monitored: true),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1),
            Now);

        Assert.IsTrue(plan.Allowed);
        Assert.IsNull(plan.SetSonarrMonitored);
        Assert.IsFalse(plan.State.Anime["frieren"].SonarrUnmonitoredByAniLingo);
        StringAssert.Contains(plan.Reason, "still monitors");
    }

    [TestMethod]
    public void RevertRestoresExactlyWhatHandOverChanged()
    {
        var handOver = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            Observed(monitored: true),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1, ApplySonarrMonitoring: true),
            Now);

        var revertRequest = new AnimeMigrationRequest("frieren", AnimeMigrationAction.Revert, ApplySonarrMonitoring: true);
        var revert = SonarrMigration.Plan(handOver.State, Observed(monitored: false), revertRequest, Now.AddHours(1));

        Assert.IsTrue(revert.Allowed);
        Assert.IsTrue(revert.Changed);
        Assert.IsTrue(revert.SetSonarrMonitored);
        var assignment = revert.State.Anime["frieren"];
        Assert.AreEqual(AnimeManagementMode.ReadOnlyCoexistence, assignment.Mode);
        Assert.IsFalse(assignment.SonarrUnmonitoredByAniLingo);
        Assert.AreEqual(1, assignment.SonarrSeriesId);

        var again = SonarrMigration.Plan(revert.State, Observed(monitored: true), revertRequest, Now.AddHours(2));
        Assert.IsTrue(again.Allowed);
        Assert.IsFalse(again.Changed);
        Assert.IsNull(again.SetSonarrMonitored);
        Assert.AreEqual(2, again.State.MigrationLog.Count);
        Assert.AreEqual(AnimeMigrationAction.HandOverToAniLingo, again.State.MigrationLog[0].Action);
        Assert.AreEqual(AnimeMigrationAction.Revert, again.State.MigrationLog[1].Action);
    }

    [TestMethod]
    public void RevertOfUntouchedAnimeIsANoOp()
    {
        var state = AcquisitionOwnershipState.Empty();
        var plan = SonarrMigration.Plan(
            state,
            Observed(monitored: true),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.Revert),
            Now);

        Assert.IsTrue(plan.Allowed);
        Assert.IsFalse(plan.Changed);
        Assert.AreSame(state, plan.State);
    }

    [TestMethod]
    public void HandOverWaitsForActiveSonarrDownloads()
    {
        var sonarr = Observed(monitored: true) with
        {
            Queue = [QueueItem(seriesId: 1, episode: 3)]
        };

        var plan = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            sonarr,
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1, ApplySonarrMonitoring: true),
            Now);

        Assert.IsFalse(plan.Allowed);
        Assert.IsNull(plan.SetSonarrMonitored);
        Assert.AreEqual(0, plan.State.Anime.Count);
    }

    [TestMethod]
    public void HandOverOfLinkedAnimeNeedsSonarrObservation()
    {
        var plan = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            SonarrObservedState.Unavailable("timeout", Now),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1),
            Now);

        Assert.IsFalse(plan.Allowed);
    }

    [TestMethod]
    public void HandOverWithoutSonarrConfiguredIsAllowed()
    {
        var plan = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            SonarrObservedState.NotConfigured,
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo),
            Now);

        Assert.IsTrue(plan.Allowed);
        Assert.AreEqual(AnimeManagementMode.AniLingoManaged, plan.State.Anime["frieren"].Mode);
    }

    [TestMethod]
    public void RevertIsBlockedWhileAniLingoJobIsActive()
    {
        var state = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            Observed(monitored: false),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1),
            Now).State;
        state = SonarrParallelSafety.RegisterJob(
            state,
            new AcquisitionOwnership("job-1", "frieren", AcquisitionOwner.AniLingo, "release", AcquisitionOwnershipStatus.Importing, Now));

        var plan = SonarrMigration.Plan(
            state,
            Observed(monitored: false),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.Revert),
            Now);

        Assert.IsFalse(plan.Allowed);
        Assert.AreEqual(AnimeManagementMode.AniLingoManaged, plan.State.Anime["frieren"].Mode);
    }

    [TestMethod]
    public void CannotRelinkWhileAniLingoStillOwesSonarrMonitoringRestore()
    {
        var state = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            Observed(monitored: true),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1, ApplySonarrMonitoring: true),
            Now).State;

        var plan = SonarrMigration.Plan(
            state,
            Observed(monitored: false),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.StartParallel, 2),
            Now);

        Assert.IsFalse(plan.Allowed);
    }

    [TestMethod]
    public void ModeChangesKeepTheSonarrLink()
    {
        var state = SonarrMigration.Plan(
            AcquisitionOwnershipState.Empty(),
            Observed(monitored: true),
            new AnimeMigrationRequest("frieren", AnimeMigrationAction.StartParallel, 1),
            Now).State;

        state = SonarrParallelSafety.SetMode(state, "frieren", AnimeManagementMode.AniLingoManaged, Now);

        Assert.AreEqual(1, state.Anime["frieren"].SonarrSeriesId);
        Assert.AreEqual("Frieren", state.Anime["frieren"].SonarrSeriesTitle);
    }

    [TestMethod]
    public async Task MigrationStateSurvivesRestartAndLegacyFilesStillLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "anilingo-sonarr-migration-" + Guid.NewGuid());
        try
        {
            var store = new AcquisitionOwnershipStore(root);
            var legacyPath = Path.Combine(root, "acquisition", "ownership.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            await File.WriteAllTextAsync(
                legacyPath,
                """
                {
                  "version": 1,
                  "anime": { "frieren": { "animeKey": "frieren", "mode": 1, "changedAtUtc": "2026-09-01T00:00:00+00:00" } },
                  "jobs": {},
                  "paths": {}
                }
                """);

            var legacy = await store.LoadAsync();
            Assert.AreEqual(AnimeManagementMode.ParallelAcquisition, legacy.Anime["FRIEREN"].Mode);
            Assert.IsNull(legacy.Anime["frieren"].SonarrSeriesId);
            Assert.AreEqual(0, legacy.MigrationLog.Count);

            await store.UpdateAsync(state => SonarrMigration.Plan(
                state,
                Observed(monitored: true),
                new AnimeMigrationRequest("frieren", AnimeMigrationAction.HandOverToAniLingo, 1, ApplySonarrMonitoring: true),
                Now).State);

            var reloaded = await new AcquisitionOwnershipStore(root).LoadAsync();
            var assignment = reloaded.Anime["frieren"];
            Assert.AreEqual(AnimeManagementMode.AniLingoManaged, assignment.Mode);
            Assert.AreEqual(1, assignment.SonarrSeriesId);
            Assert.IsTrue(assignment.SonarrUnmonitoredByAniLingo);
            Assert.AreEqual(1, reloaded.MigrationLog.Count);
            Assert.AreEqual(AnimeManagementMode.ParallelAcquisition, reloaded.MigrationLog[0].FromMode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    internal static SonarrObservedState Observed(bool monitored) =>
        SonarrObservedState.FromObservation(
            [
                new SonarrObservedSeries(1, "Frieren", "/tv/anime/Frieren", monitored),
                new SonarrObservedSeries(2, "Dungeon Meshi", "/tv/anime/Dungeon Meshi", true)
            ],
            [],
            [],
            [],
            Now);

    internal static SonarrObservedQueueItem QueueItem(int seriesId, int episode, string? downloadId = null) =>
        new(
            100 + episode,
            seriesId,
            $"Queued - S01E{episode:00} WEB-DL 1080p",
            $"queued-s01e{episode:00}",
            downloadId ?? $"sonarr-nzo-{seriesId}-{episode}",
            $"/downloads/complete/tv/series{seriesId}.e{episode:00}",
            "downloading",
            "downloading",
            new SonarrObservedEpisode(1, episode, episode));
}
