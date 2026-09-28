using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Jularr.Tests;

// Issue #406: the one write/read path for durable artwork kept beside a work's media on a NAS
// library root, shared by every non-anime media type (Books covers today).
[TestClass]
public sealed class BesideMediaArtworkStoreTests
{
    private const string Scope = "test-scope";
    private const string Kind = "cover";

    [TestMethod]
    public async Task ProviderArtworkIsWrittenBesideTheMediaAndRecorded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var bytes = Jpeg();

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, bytes, "provider", "https://example/1.jpg", CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.Saved, outcome);
        var path = Path.Combine(fixture.Folder, "cover.jpg");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        var record = (await fixture.Assets.ListAsync(Scope, fixture.OwnerId, CancellationToken.None)).Single();
        Assert.AreEqual("cover.jpg", record.FileName);
        Assert.AreEqual("provider", record.Source);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.Folder, "*.tmp").Any(), "No temporary files remain beside the media.");
    }

    [TestMethod]
    public async Task UnchangedArtworkIsNotRewritten()
    {
        await using var fixture = await Fixture.CreateAsync();
        var bytes = Jpeg();
        await fixture.Store.PersistAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, bytes, "provider", "https://example/1.jpg", CancellationToken.None);

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, bytes, "provider", "https://example/1.jpg", CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.Current, outcome);
    }

    [TestMethod]
    public async Task UserArtworkBesideTheMediaIsNeverReplaced()
    {
        await using var fixture = await Fixture.CreateAsync();
        var custom = Png();
        var customPath = Path.Combine(fixture.Folder, "cover.png");
        await File.WriteAllBytesAsync(customPath, custom);

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, Jpeg(), "provider", "https://example/1.jpg", CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.KeptCustom, outcome);
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(customPath));
        Assert.AreEqual(1, Directory.EnumerateFiles(fixture.Folder).Count(), "No provider copy is added next to the user's cover.");
    }

    [TestMethod]
    public async Task ChangedArtworkReplacesTheOldFileWithoutAStaleExtension()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.PersistAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, Jpeg(), "provider", "https://example/1.jpg", CancellationToken.None);

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, Png(), "provider", "https://example/2.png", CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.Saved, outcome);
        CollectionAssert.AreEqual(
            new[] { "cover.png" },
            Directory.EnumerateFiles(fixture.Folder).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    public async Task ReplacingAJularrFileMakesItTheUsersOwn()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.PersistAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, Jpeg(), "provider", "https://example/1.jpg", CancellationToken.None);
        var canonical = Path.Combine(fixture.Folder, "cover.jpg");
        // A different size than the originally-persisted image, so it is never mistaken for an
        // unchanged managed file (same rationale as MediaArtworkAsset.Matches' timestamp tolerance).
        var custom = Image(320, 480, SKEncodedImageFormat.Jpeg);
        await File.WriteAllBytesAsync(canonical, custom);

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, Image(610, 900, SKEncodedImageFormat.Jpeg), "provider", "https://example/2.jpg", CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.KeptCustom, outcome);
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(canonical));
    }

    [TestMethod]
    public async Task UnavailableFolderChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        Directory.Delete(fixture.Folder);

        var outcome = await fixture.Store.PersistAsync(
            Scope, fixture.OwnerId, Kind, fixture.Folder, Jpeg(), "provider", "https://example/1.jpg", CancellationToken.None);
        var resolved = await fixture.Store.ResolveAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, CancellationToken.None);

        Assert.AreEqual(ArtworkPersistOutcome.Unavailable, outcome);
        Assert.IsNull(resolved);
    }

    [TestMethod]
    public async Task ResolveFindsTheCanonicalFileOverAnUnmanagedOne()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.PersistAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, Jpeg(), "provider", "https://example/1.jpg", CancellationToken.None);

        var resolved = await fixture.Store.ResolveAsync(Scope, fixture.OwnerId, Kind, fixture.Folder, CancellationToken.None);

        Assert.AreEqual(Path.Combine(fixture.Folder, "cover.jpg"), resolved);
    }

    private static byte[] Jpeg() => Image(600, 900, SKEncodedImageFormat.Jpeg);

    private static byte[] Png() => Image(300, 450, SKEncodedImageFormat.Png);

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string tempRoot, AppDbContext db)
        {
            TempRoot = tempRoot;
            Db = db;
            Store = new BesideMediaArtworkStore(db);
            Assets = new MediaArtworkAssetStore(db);
        }

        public string TempRoot { get; }
        public AppDbContext Db { get; }
        public BesideMediaArtworkStore Store { get; }
        public MediaArtworkAssetStore Assets { get; }
        public Guid OwnerId { get; } = Guid.NewGuid();
        public string Folder => Path.Combine(TempRoot, "media", "Some Book");

        public static async Task<Fixture> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"jularr-beside-media-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "media", "Some Book"));
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(tempRoot, "jularr.db")};Pooling=False")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(tempRoot, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(TempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
