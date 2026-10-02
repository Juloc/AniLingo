using Jularr.Web.Features.ClientApi;
using Jularr.Web.Features.Instance;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Microsoft.AspNetCore.Http;

namespace Jularr.Tests;

[TestClass]
public sealed class InstanceModuleTests
{
    [TestMethod]
    public async Task FreshStoreEnablesEverythingAndPersistsSwitches()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"jularr-instance-modules-{Guid.NewGuid():N}");

        try
        {
            var store = new InstanceModuleStore(root);
            var initial = await store.GetAsync();

            foreach (var module in Enum.GetValues<InstanceModule>())
            {
                Assert.IsTrue(initial.IsEnabled(module), module.ToString());
            }

            await store.SetAsync(InstanceModule.Learning, false);
            Assert.IsFalse(await store.IsEnabledAsync(InstanceModule.Learning));

            var reloaded = await new InstanceModuleStore(root).GetAsync();
            Assert.IsFalse(reloaded.IsEnabled(InstanceModule.Learning));
            Assert.IsTrue(reloaded.IsEnabled(InstanceModule.Anime));
            Assert.IsTrue(reloaded.IsEnabled(InstanceModule.Acquisition));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ExplicitModuleRoutesResolveToTheGlobalModule()
    {
        foreach (var path in new[]
                 {
                     "/Learn",
                     "/Learn/Review",
                     "/Kana",
                     "/Statistics",
                     "/Settings/Learning",
                     "/Settings/LearningCourses",
                     "/Settings/LearningScope",
                     "/api/language-inspector/inspect"
                 })
        {
            Assert.IsTrue(
                InstanceModuleRoutes.TryResolve(new PathString(path), out var module),
                path);
            Assert.AreEqual(InstanceModule.Learning, module, path);
        }

        foreach (var path in new[]
                 {
                     "/Acquisition",
                     "/Acquisition/Search",
                     "/api/acquisition/v1/anime"
                 })
        {
            Assert.IsTrue(
                InstanceModuleRoutes.TryResolve(new PathString(path), out var module),
                path);
            Assert.AreEqual(InstanceModule.Anime, module, path);

            CollectionAssert.AreEquivalent(
                new[] { InstanceModule.Anime, InstanceModule.Acquisition },
                InstanceModuleRoutes.Resolve(new PathString(path)).ToArray(),
                path);
        }

        CollectionAssert.Contains(
            InstanceModuleRoutes.Resolve(new PathString("/Admin/Requests")).ToArray(),
            InstanceModule.Acquisition);
        CollectionAssert.AreEquivalent(
            new[] { InstanceModule.Tracking },
            InstanceModuleRoutes.Resolve(new PathString("/Settings/AniList")).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { InstanceModule.Manga },
            InstanceModuleRoutes.Resolve(new PathString("/Discover/MangaImport")).ToArray());

        Assert.IsFalse(
            InstanceModuleRoutes.TryResolve(
                new PathString("/Library/Anime/123"),
                out _));
    }

    [TestMethod]
    public void CanonicalMediaTypesMapToInstanceModules()
    {
        Assert.AreEqual(InstanceModule.Movie, InstanceModuleMedia.For(WorkMediaType.Movie));
        Assert.AreEqual(InstanceModule.Tv, InstanceModuleMedia.For(WorkMediaType.Series));
        Assert.AreEqual(InstanceModule.Anime, InstanceModuleMedia.For(WorkMediaType.Anime));
        Assert.AreEqual(InstanceModule.Book, InstanceModuleMedia.For(WorkMediaType.Book));
        Assert.AreEqual(InstanceModule.Manga, InstanceModuleMedia.For(WorkMediaType.Manga));
        Assert.AreEqual(InstanceModule.Novel, InstanceModuleMedia.For(WorkMediaType.LightNovel));
    }

    [TestMethod]
    public void DisabledLearningDisappearsFromShellAndSettings()
    {
        var enabled = Enum.GetValues<InstanceModule>()
            .Where(module => module != InstanceModule.Learning)
            .ToHashSet();

        var shell = UiShellNavigation.Build(
            "/",
            learningVisible: true,
            can: _ => true,
            enabledInstanceModules: enabled);

        Assert.IsFalse(shell.Primary.Any(item => item.Id == "learn"));

        var settings = UiShellNavigation.BuildSection(
            "settings",
            can: _ => true,
            enabledInstanceModules: enabled);

        Assert.IsNotNull(settings);
        Assert.IsFalse(
            settings.Groups!
                .SelectMany(group => group.Items)
                .Any(item => item.Id == "settings-learning"));
    }

    [TestMethod]
    public void DisabledTrackingDisappearsFromSettingsNavigation()
    {
        var enabled = Enum.GetValues<InstanceModule>()
            .Where(module => module != InstanceModule.Tracking)
            .ToHashSet();

        var settings = UiShellNavigation.BuildSection(
            "settings",
            can: _ => true,
            enabledInstanceModules: enabled);

        Assert.IsNotNull(settings);
        Assert.IsFalse(
            settings.Groups!
                .SelectMany(group => group.Items)
                .Any(item => item.Id == "settings-anilist"));
    }

    [TestMethod]
    public void DisabledAcquisitionDisappearsFromAdminNavigation()
    {
        var enabled = Enum.GetValues<InstanceModule>()
            .Where(module => module != InstanceModule.Acquisition)
            .ToHashSet();

        var admin = UiShellNavigation.BuildSection(
            "admin",
            can: _ => true,
            enabledInstanceModules: enabled);

        Assert.IsNotNull(admin);
        var ids = admin.Groups!
            .SelectMany(group => group.Items)
            .Select(item => item.Id)
            .ToArray();

        CollectionAssert.DoesNotContain(ids, "admin-requests");
        CollectionAssert.DoesNotContain(ids, "admin-usenet");
        CollectionAssert.DoesNotContain(ids, "admin-anime-acquisition");
        CollectionAssert.DoesNotContain(ids, "admin-import");
        CollectionAssert.DoesNotContain(ids, "admin-sonarr");
    }

    [TestMethod]
    public void ClientCapabilitiesStopAdvertisingLearningWhenDisabled()
    {
        var settings = InstanceModuleSettings.Default.With(
            InstanceModule.Learning,
            false);

        var capabilities = ClientApiContract.Capabilities(settings);

        Assert.IsFalse(capabilities.Features.NormalizedLearningCues);
        Assert.IsFalse(capabilities.Features.LearningStateMutation);
        Assert.IsTrue(capabilities.Features.Library);
        Assert.IsTrue(capabilities.Features.DirectPlayback);
    }
}
