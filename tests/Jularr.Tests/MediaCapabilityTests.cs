using System.Security.Claims;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaCapabilityTests
{
    [TestMethod]
    public void CapabilityLadderIsOrderedHiddenBrowseRequestInstant()
    {
        // Sorting the enum by its numeric value must yield the intended ladder order, so callers can
        // compare capabilities with `>=` (as the guard and the view do).
        CollectionAssert.AreEqual(
            new[] { MediaCapability.Hidden, MediaCapability.Browse, MediaCapability.Request, MediaCapability.Instant },
            Enum.GetValues<MediaCapability>().OrderBy(value => (int)value).ToArray());
    }

    [TestMethod]
    public void DefaultsGiveMediaManagerInstantAndUserRequest()
    {
        var policy = MediaCapabilityPolicy.Default;

        foreach (var mediaType in WorkMediaTypes.All)
        {
            Assert.AreEqual(MediaCapability.Instant, policy.RoleDefault(AccountRole.MediaManager, mediaType), mediaType.ToString());
            Assert.AreEqual(MediaCapability.Request, policy.RoleDefault(AccountRole.User, mediaType), mediaType.ToString());
        }
    }

    [TestMethod]
    public void PerUserOverrideWinsOverRoleDefault()
    {
        var policy = MediaCapabilityPolicy.Default
            .WithRoleDefault(AccountRole.User, WorkMediaType.Anime, MediaCapability.Browse)
            .WithUserOverride("alice", WorkMediaType.Anime, MediaCapability.Instant);

        // Another user with the same role still gets the role default.
        Assert.AreEqual(MediaCapability.Browse, policy.Resolve(AccountRole.User, "bob", WorkMediaType.Anime));
        // The overridden user gets the override.
        Assert.AreEqual(MediaCapability.Instant, policy.Resolve(AccountRole.User, "alice", WorkMediaType.Anime));
        // A media type without an override falls back to the role default.
        Assert.AreEqual(MediaCapability.Request, policy.Resolve(AccountRole.User, "alice", WorkMediaType.Book));
    }

    [TestMethod]
    public void OwnerIsUnrestrictedRegardlessOfPolicy()
    {
        var policy = MediaCapabilityPolicy.Default
            .WithRoleDefault(AccountRole.User, WorkMediaType.Movie, MediaCapability.Hidden)
            .WithRoleDefault(AccountRole.MediaManager, WorkMediaType.Movie, MediaCapability.Hidden);

        foreach (var mediaType in WorkMediaTypes.All)
        {
            Assert.AreEqual(MediaCapability.Instant, policy.Resolve(AccountRole.Owner, "someone", mediaType), mediaType.ToString());
        }

        Assert.ThrowsExactly<InvalidOperationException>(
            () => policy.WithRoleDefault(AccountRole.Owner, WorkMediaType.Movie, MediaCapability.Hidden));
    }

    [TestMethod]
    public void ClearingAnOverrideRestoresTheRoleDefault()
    {
        var policy = MediaCapabilityPolicy.Default
            .WithUserOverride("alice", WorkMediaType.Manga, MediaCapability.Hidden);
        Assert.AreEqual(MediaCapability.Hidden, policy.Resolve(AccountRole.User, "alice", WorkMediaType.Manga));

        policy = policy.WithUserOverride("alice", WorkMediaType.Manga, capability: null);
        Assert.IsNull(policy.UserOverride("alice", WorkMediaType.Manga));
        Assert.AreEqual(MediaCapability.Request, policy.Resolve(AccountRole.User, "alice", WorkMediaType.Manga));
    }

    [TestMethod]
    public void ViewHidesHiddenTypesAndGatesRequestVersusInstant()
    {
        var capabilities = new Dictionary<WorkMediaType, MediaCapability>
        {
            [WorkMediaType.Movie] = MediaCapability.Hidden,
            [WorkMediaType.Series] = MediaCapability.Browse,
            [WorkMediaType.Anime] = MediaCapability.Request,
            [WorkMediaType.Book] = MediaCapability.Instant,
            [WorkMediaType.Manga] = MediaCapability.Hidden,
            [WorkMediaType.LightNovel] = MediaCapability.Request
        };
        var view = new MediaCapabilityView(IsOwner: false, capabilities);

        CollectionAssert.AreEqual(
            new[] { WorkMediaType.Series, WorkMediaType.Anime, WorkMediaType.Book, WorkMediaType.LightNovel },
            view.VisibleMediaTypes.ToArray());

        Assert.IsFalse(view.CanBrowse(WorkMediaType.Movie));
        Assert.IsTrue(view.CanBrowse(WorkMediaType.Series));
        Assert.IsFalse(view.CanRequest(WorkMediaType.Series));
        Assert.IsTrue(view.CanRequest(WorkMediaType.Anime));
        Assert.IsFalse(view.CanUseInstantly(WorkMediaType.Anime));
        Assert.IsTrue(view.CanUseInstantly(WorkMediaType.Book));
    }

    [TestMethod]
    public async Task StorePersistsRoleDefaultsAndUserOverridesAndDefaultsWhenMissing()
    {
        var directory = NewDirectory();
        try
        {
            var store = new MediaCapabilityStore(directory);

            // No file yet: the built-in defaults are returned.
            var initial = await store.LoadAsync();
            Assert.AreEqual(MediaCapability.Request, initial.RoleDefault(AccountRole.User, WorkMediaType.Anime));

            await store.SetRoleDefaultAsync(AccountRole.User, WorkMediaType.Movie, MediaCapability.Hidden);
            await store.SetUserOverrideAsync("alice", WorkMediaType.Anime, MediaCapability.Instant);

            // A fresh store instance reads the persisted file, not in-memory state.
            var reloaded = await new MediaCapabilityStore(directory).LoadAsync();
            Assert.AreEqual(MediaCapability.Hidden, reloaded.RoleDefault(AccountRole.User, WorkMediaType.Movie));
            Assert.AreEqual(MediaCapability.Request, reloaded.RoleDefault(AccountRole.User, WorkMediaType.Book));
            Assert.AreEqual(MediaCapability.Instant, reloaded.UserOverride("alice", WorkMediaType.Anime));

            await store.ClearUserAsync("alice");
            Assert.IsNull((await new MediaCapabilityStore(directory).LoadAsync()).UserOverride("alice", WorkMediaType.Anime));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ServiceResolvesRoleAndProfileFromPrincipal()
    {
        var directory = NewDirectory();
        try
        {
            var store = new MediaCapabilityStore(directory);
            await store.SaveAsync(MediaCapabilityPolicy.Default
                .WithRoleDefault(AccountRole.User, WorkMediaType.Movie, MediaCapability.Hidden)
                .WithUserOverride("alice", WorkMediaType.Movie, MediaCapability.Request));
            var service = new MediaCapabilityService(store);

            // Plain user "bob" inherits the User role default: Movie hidden.
            var bob = await service.GetViewAsync(Principal(AccountRole.User, "bob"));
            Assert.IsFalse(bob.IsOwner);
            Assert.AreEqual(MediaCapability.Hidden, bob.Capability(WorkMediaType.Movie));
            CollectionAssert.DoesNotContain(bob.VisibleMediaTypes.ToArray(), WorkMediaType.Movie);

            // "alice" has a per-user override lifting Movie to Request.
            Assert.AreEqual(MediaCapability.Request,
                await service.GetEffectiveCapabilityAsync(Principal(AccountRole.User, "alice"), WorkMediaType.Movie));

            // A media manager gets the Instant role default across the board.
            var manager = await service.GetViewAsync(Principal(AccountRole.MediaManager, "mm"));
            Assert.AreEqual(MediaCapability.Instant, manager.Capability(WorkMediaType.Movie));

            // The owner is unrestricted regardless of the stored policy.
            var owner = await service.GetViewAsync(Principal(AccountRole.Owner, "owner"));
            Assert.IsTrue(owner.IsOwner);
            Assert.IsTrue(WorkMediaTypes.All.All(type => owner.Capability(type) == MediaCapability.Instant));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InstanceModuleOverridesOwnerAndProfileCapabilities()
    {
        var directory = NewDirectory();
        var instanceRoot = Path.Combine(
            Path.GetTempPath(),
            $"jularr-instance-cap-{Guid.NewGuid():N}");
        try
        {
            var modules = new InstanceModuleStore(instanceRoot);
            await modules.SetAsync(InstanceModule.Anime, false);
            var service = new MediaCapabilityService(
                new MediaCapabilityStore(directory),
                modules);

            var owner = await service.GetViewAsync(
                Principal(AccountRole.Owner, "owner"));
            Assert.AreEqual(
                MediaCapability.Hidden,
                owner.Capability(WorkMediaType.Anime));
            Assert.AreEqual(
                MediaCapability.Instant,
                owner.Capability(WorkMediaType.Book));

            var user = await service.GetViewAsync(
                Principal(AccountRole.User, "user"));
            Assert.AreEqual(
                MediaCapability.Hidden,
                user.Capability(WorkMediaType.Anime));
            Assert.AreEqual(
                MediaCapability.Request,
                user.Capability(WorkMediaType.Book));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            if (Directory.Exists(instanceRoot))
            {
                Directory.Delete(instanceRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task UnauthenticatedPrincipalSeesNothing()
    {
        var directory = NewDirectory();
        try
        {
            var service = new MediaCapabilityService(new MediaCapabilityStore(directory));

            var anonymous = await service.GetViewAsync(new ClaimsPrincipal(new ClaimsIdentity()));
            Assert.IsFalse(anonymous.IsOwner);
            Assert.AreEqual(0, anonymous.VisibleMediaTypes.Count);
            Assert.IsTrue(WorkMediaTypes.All.All(type => anonymous.Capability(type) == MediaCapability.Hidden));

            Assert.AreEqual(0, (await service.GetVisibleMediaTypesAsync(null)).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GuardAllowsAtOrAboveRequiredAndRefusesBelow()
    {
        var directory = NewDirectory();
        try
        {
            var store = new MediaCapabilityStore(directory);
            await store.SaveAsync(MediaCapabilityPolicy.Default
                .WithRoleDefault(AccountRole.User, WorkMediaType.Anime, MediaCapability.Request));
            var service = new MediaCapabilityService(store);
            var user = Principal(AccountRole.User, "bob");

            // Request >= Browse and Request >= Request both pass.
            await service.EnsureCapabilityAsync(user, WorkMediaType.Anime, MediaCapability.Browse);
            await service.EnsureCapabilityAsync(user, WorkMediaType.Anime, MediaCapability.Request);

            // Request < Instant is refused with the typed guard exception.
            var denied = await Assert.ThrowsExactlyAsync<MediaCapabilityDeniedException>(
                () => service.EnsureCapabilityAsync(user, WorkMediaType.Anime, MediaCapability.Instant));
            Assert.AreEqual(WorkMediaType.Anime, denied.MediaType);
            Assert.AreEqual(MediaCapability.Instant, denied.Required);
            Assert.AreEqual(MediaCapability.Request, denied.Actual);

            // The owner passes the same guard unconditionally.
            await service.EnsureCapabilityAsync(Principal(AccountRole.Owner, "owner"), WorkMediaType.Anime, MediaCapability.Instant);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CapabilityNamesRoundTripAndIgnoreUnknownValues()
    {
        foreach (var capability in Enum.GetValues<MediaCapability>())
        {
            Assert.AreEqual(capability, MediaCapabilityNames.Parse(MediaCapabilityNames.ToStorage(capability)));
            Assert.AreEqual(capability, MediaCapabilityNames.TryParse(MediaCapabilityNames.ToStorage(capability)));
        }

        Assert.IsNull(MediaCapabilityNames.TryParse(null));
        Assert.IsNull(MediaCapabilityNames.TryParse(""));
        Assert.IsNull(MediaCapabilityNames.TryParse("nonsense"));
        Assert.ThrowsExactly<ArgumentException>(() => MediaCapabilityNames.Parse("nonsense"));
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-cap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ClaimsPrincipal Principal(AccountRole role, string profileId) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, profileId),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            authenticationType: "test"));
}
