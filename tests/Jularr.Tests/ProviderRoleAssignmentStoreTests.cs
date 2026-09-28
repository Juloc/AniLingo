using Jularr.Web.Data;
using Jularr.Web.Features.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class ProviderRoleAssignmentStoreTests
{
    [TestMethod]
    public async Task Defaults_ReproduceTodaysBehaviourWhenNothingStored()
    {
        await using var fixture = await Fixture.CreateAsync();

        var global = await fixture.Store.ResolveGlobalDefaultsAsync(CancellationToken.None);
        var byRole = global.ToDictionary(x => x.Role, x => x.Provider);

        Assert.AreEqual(MappingProviders.AniList, byRole[MappingProviderRole.DisplayMetadata]);
        Assert.AreEqual(MappingProviders.AniList, byRole[MappingProviderRole.ProgressTracking]);
        Assert.AreEqual(MappingProviders.AniList, byRole[MappingProviderRole.Artwork]);
        Assert.AreEqual(MappingProviders.Local, byRole[MappingProviderRole.EpisodeStructure]);
        Assert.AreEqual(MappingProviders.Local, byRole[MappingProviderRole.AcquisitionIdentity]);
        Assert.AreEqual(MappingProviders.Tvdb, byRole[MappingProviderRole.CrossReferenceIds]);
        Assert.IsTrue(global.All(x => x.Source == ProviderRoleSource.BuiltIn));
    }

    [TestMethod]
    public async Task GlobalDefault_OverridesBuiltInAndFallsThroughToWork()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.SetGlobalDefaultAsync(
            MappingProviderRole.DisplayMetadata, MappingProviders.Tvdb, CancellationToken.None);

        var resolved = await fixture.Store.ResolveForWorkAsync(work, CancellationToken.None);
        var display = resolved.Single(x => x.Role == MappingProviderRole.DisplayMetadata);

        Assert.AreEqual(MappingProviders.Tvdb, display.Provider);
        Assert.AreEqual(ProviderRoleSource.GlobalDefault, display.Source);
    }

    [TestMethod]
    public async Task WorkOverride_WinsOverGlobalDefault()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.SetGlobalDefaultAsync(
            MappingProviderRole.DisplayMetadata, MappingProviders.Tvdb, CancellationToken.None);
        await fixture.Store.SetWorkOverrideAsync(
            work, MappingProviderRole.DisplayMetadata, MappingProviders.Mal, CancellationToken.None);

        var resolved = await fixture.Store.ResolveForWorkAsync(work, CancellationToken.None);
        var display = resolved.Single(x => x.Role == MappingProviderRole.DisplayMetadata);

        Assert.AreEqual(MappingProviders.Mal, display.Provider);
        Assert.AreEqual(ProviderRoleSource.WorkOverride, display.Source);

        // A different work still inherits the global default.
        var other = await fixture.Store.ResolveForWorkAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.AreEqual(MappingProviders.Tvdb, other.Single(x => x.Role == MappingProviderRole.DisplayMetadata).Provider);
    }

    [TestMethod]
    public async Task ClearWorkOverride_FallsBackToGlobal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        await fixture.Store.SetWorkOverrideAsync(
            work, MappingProviderRole.Artwork, MappingProviders.Tmdb, CancellationToken.None);
        await fixture.Store.ClearWorkOverrideAsync(
            work, MappingProviderRole.Artwork, CancellationToken.None);

        var resolved = await fixture.Store.ResolveForWorkAsync(work, CancellationToken.None);
        var artwork = resolved.Single(x => x.Role == MappingProviderRole.Artwork);
        Assert.AreEqual(MappingProviders.AniList, artwork.Provider);
        Assert.AreEqual(ProviderRoleSource.BuiltIn, artwork.Source);
    }

    [TestMethod]
    public async Task SettingGlobalToBuiltIn_ClearsTheStoredRow()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Store.SetGlobalDefaultAsync(
            MappingProviderRole.DisplayMetadata, MappingProviders.Tvdb, CancellationToken.None);
        await fixture.Store.SetGlobalDefaultAsync(
            MappingProviderRole.DisplayMetadata, MappingProviders.AniList, CancellationToken.None);

        var display = (await fixture.Store.ResolveGlobalDefaultsAsync(CancellationToken.None))
            .Single(x => x.Role == MappingProviderRole.DisplayMetadata);
        Assert.AreEqual(ProviderRoleSource.BuiltIn, display.Source);
    }

    [TestMethod]
    public async Task RejectsProviderNotAllowedForRole()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Store.SetGlobalDefaultAsync(
                MappingProviderRole.ProgressTracking, MappingProviders.Imdb, CancellationToken.None));
    }

    // Issue #568: feature services resolve a single role through ResolveRoleForWorkAsync instead
    // of hard-wiring a provider; it must agree with the full ResolveForWorkAsync resolution.
    [TestMethod]
    public async Task ResolveRoleForWork_ReturnsTheSingleRequestedRole()
    {
        await using var fixture = await Fixture.CreateAsync();
        var work = Guid.NewGuid();

        var unset = await fixture.Store.ResolveRoleForWorkAsync(
            work, MappingProviderRole.Artwork, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, unset.Provider);
        Assert.AreEqual(ProviderRoleSource.BuiltIn, unset.Source);

        await fixture.Store.SetWorkOverrideAsync(
            work, MappingProviderRole.Artwork, MappingProviders.Tmdb, CancellationToken.None);

        var overridden = await fixture.Store.ResolveRoleForWorkAsync(
            work, MappingProviderRole.Artwork, CancellationToken.None);
        Assert.AreEqual(MappingProviders.Tmdb, overridden.Provider);
        Assert.AreEqual(ProviderRoleSource.WorkOverride, overridden.Source);

        // A different role for the same work is unaffected by the Artwork override.
        var display = await fixture.Store.ResolveRoleForWorkAsync(
            work, MappingProviderRole.DisplayMetadata, CancellationToken.None);
        Assert.AreEqual(MappingProviders.AniList, display.Provider);
        Assert.AreEqual(ProviderRoleSource.BuiltIn, display.Source);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly AppDbContext _db;

        private Fixture(string databasePath, AppDbContext db)
        {
            _databasePath = databasePath;
            _db = db;
            Store = new ProviderRoleAssignmentStore(db);
        }

        public ProviderRoleAssignmentStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jularr-roles-{Guid.NewGuid():N}.db");
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(databasePath, db);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            try
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
