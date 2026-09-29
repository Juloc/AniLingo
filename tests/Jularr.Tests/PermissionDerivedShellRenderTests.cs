using Jularr.Web.Features.Auth;
using Jularr.Web.Features.MediaCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jularr.Tests;

/// <summary>
/// The rendered shell (#598): the real <c>_AppNavigation</c> partial reads the profile's media
/// types from <c>IAppShellService</c>, so a media type the owner hid never reaches the sidebar HTML.
/// </summary>
[TestClass]
public sealed class PermissionDerivedShellRenderTests
{
    [TestMethod]
    public async Task ASidebarForAProfileThatOnlySeesBooksOpensTheBookLibrary()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<MediaCapabilityStore>();
        foreach (var type in WorkMediaTypes.All.Where(type => type != WorkMediaType.Book))
        {
            await store.SetRoleDefaultAsync(AccountRole.MediaManager, type, MediaCapability.Hidden);
        }

        var sidebar = Sidebar(await host.GetHtmlAsync("/Settings/Acquisition", asOwner: false, asMediaManager: true));

        StringAssert.Contains(sidebar, "href=\"/Books\"");
        foreach (var hidden in new[] { "/Library", "/Reading", "/Novels", "/Manga" })
        {
            Assert.IsFalse(sidebar.Contains($"href=\"{hidden}", StringComparison.Ordinal), $"{hidden} must not be in the sidebar.");
        }
    }

    [TestMethod]
    public async Task TheOwnersSidebarKeepsTheFullLibraryWhateverThePolicySays()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<MediaCapabilityStore>();
        foreach (var type in WorkMediaTypes.All)
        {
            await store.SetRoleDefaultAsync(AccountRole.MediaManager, type, MediaCapability.Hidden);
            await store.SetRoleDefaultAsync(AccountRole.User, type, MediaCapability.Hidden);
        }

        var sidebar = Sidebar(await host.GetHtmlAsync("/Settings/Acquisition", asOwner: true));

        StringAssert.Contains(sidebar, "href=\"/Library\"");
    }

    [TestMethod]
    public async Task ASidebarForAProfileWithoutAnyMediaTypeHasNoLibrary()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        var store = host.Services.GetRequiredService<MediaCapabilityStore>();
        foreach (var type in WorkMediaTypes.All)
        {
            await store.SetRoleDefaultAsync(AccountRole.MediaManager, type, MediaCapability.Hidden);
        }

        var sidebar = Sidebar(await host.GetHtmlAsync("/Settings/Acquisition", asOwner: false, asMediaManager: true));

        foreach (var hidden in new[] { "/Library", "/Reading", "/Novels", "/Manga", "/Books" })
        {
            Assert.IsFalse(sidebar.Contains($"href=\"{hidden}", StringComparison.Ordinal), $"{hidden} must not be in the sidebar.");
        }

        StringAssert.Contains(sidebar, "href=\"/Watchlist\"", "Destinations that are not media-scoped stay.");
    }

    private static string Sidebar(string html)
    {
        var start = html.IndexOf("<aside class=\"sidebar\">", StringComparison.Ordinal);
        var end = html.IndexOf("</aside>", start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, "The page must render the shell sidebar.");
        return html[start..end];
    }
}
