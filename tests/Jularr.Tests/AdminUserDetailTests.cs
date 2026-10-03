namespace Jularr.Tests;

/// <summary>
/// Regression coverage for the Admin → Users → user detail information architecture.
/// Account administration must not require loading media/Learning progress.
/// </summary>
[TestClass]
public sealed class AdminUserDetailTests
{
    [TestMethod]
    public void DetailFocusesOnAccountAccessAndSecurity()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "User.cshtml"));
        var model = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "User.cshtml.cs"));

        StringAssert.Contains(page, "data-admin-user-detail");
        StringAssert.Contains(page, "admin.user.account");
        StringAssert.Contains(page, "admin.user.access");
        StringAssert.Contains(page, "admin.user.security");
        StringAssert.Contains(page, "asp-page-handler=\"Rename\"");
        StringAssert.Contains(page, "asp-page-handler=\"SetRole\"");
        StringAssert.Contains(page, "asp-page-handler=\"SetEnabled\"");
        StringAssert.Contains(page, "asp-page-handler=\"ResetPassword\"");
        StringAssert.Contains(page, "asp-page-handler=\"InvalidateSessions\"");
        StringAssert.Contains(page, "asp-page-handler=\"Delete\"");
        StringAssert.Contains(page, "asp-page=\"/Admin/Capabilities/Index\"");

        Assert.IsFalse(page.Contains("user-progress-grid", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("CurrentAnime", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("CurrentNovel", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains(".Learning", StringComparison.Ordinal));

        StringAssert.Contains(model, "authService.GetAsync(id, cancellationToken)");
        Assert.IsFalse(model.Contains("AdminUserProgressService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CapabilityEditorHasStablePerUserAnchors()
    {
        var page = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "Jularr.Web",
                "Pages",
                "Admin",
                "Capabilities",
                "Index.cshtml"));

        StringAssert.Contains(page, "id=\"user-@user.Id\"");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }
}
