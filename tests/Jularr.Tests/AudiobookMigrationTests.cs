using Jularr.Web.Features.Acquisition.Access;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>
/// The AudiobookFoundation migration (#440): the new per-type tables exist, and the raw-SQL acquisition
/// tables now accept the audiobook kind (their "Kind" CHECK constraints were widened). The test database
/// is PostgreSQL (the canonical provider), so these assertions cover the migration's provider-specific
/// CHECK widening.
/// </summary>
[TestClass]
public sealed class AudiobookMigrationTests
{
    [TestMethod]
    public async Task NewAudiobookTablesExist()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        Assert.AreEqual(0, await db.Audiobooks.CountAsync());
        Assert.AreEqual(0, await db.AudiobookFiles.CountAsync());
        Assert.AreEqual(0, await db.AudiobookProgress.CountAsync());
    }

    [TestMethod]
    public async Task AccessPoliciesAndRequestsAcceptAudiobookKind()
    {
        await using var db = await MediaCoreTestSupport.CreateDbAsync();
        var store = new AcquisitionAccessStore(db);

        // The widened CHECK constraint on AcquisitionAccessPolicies accepts 'audiobook'.
        await store.SavePolicyAsync(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Audiobook, ManualAddMode.Users), CancellationToken.None);
        Assert.AreEqual(
            ManualAddMode.Users,
            (await store.GetPolicyAsync(MediaAcquisitionKind.Audiobook, CancellationToken.None)).Manual);

        // The widened CHECK constraint on AcquisitionRequests accepts 'audiobook'.
        var request = await store.CreateAsync(
            new AcquisitionRequestDraft(
                MediaAcquisitionKind.Audiobook, "audible", "B0TEST", "Dune", "Frank Herbert", null),
            requestedByProfileId: "reader-1",
            AcquisitionRequestStatus.Pending,
            decidedByProfileId: null,
            CancellationToken.None);

        var stored = await store.GetAsync(request.Id, CancellationToken.None);
        Assert.IsNotNull(stored);
        Assert.AreEqual(MediaAcquisitionKind.Audiobook, stored!.Kind);
    }
}
